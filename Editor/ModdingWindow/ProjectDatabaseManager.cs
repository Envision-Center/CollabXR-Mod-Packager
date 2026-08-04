using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CollabXR.ModPackager
{
	public class ProjectDatabaseManager
	{
		const string ProjectDatabasePath = "Assets/ProjectDatabase.asset";

		public ProjectDatabase ProjectDatabase = null;

		public bool IsCorrupt = false;

		public delegate void OnDatabaseLoadEvent();
		public event OnDatabaseLoadEvent OnDatabaseLoad;

		public delegate void OnCorruptionDetectedEvent();
		public event OnCorruptionDetectedEvent OnCorruptionDetected;

		public delegate void OnPostAssetPostProcessEvent();
		public event OnPostAssetPostProcessEvent OnPostAssetPostProcess;

		public ProjectDatabaseManager()
		{
			Logger.VerboseInfo("Database Constructor!");
			LoadProjectDatabaseIfNeeded(true);

			AssetPostProcessListener.OnAssetPostProcessEvent += AssetPostProcessCallback;
		}

		///
		/// ACCESSOR METHODS
		/// 

		public ModMetadata TryGetModMetadata(string assetBundleName)
		{
			if (ProjectDatabase == null || IsCorrupt)
				return null;

			if (ProjectDatabase.AssetbundleToModMap.TryGetValue(assetBundleName, out ModMetadata mod))
			{
				return mod;
			}

			return null;
		}
		public ModEditorData TryGetModEditorData(string assetBundleName)
		{
			if (ProjectDatabase == null || IsCorrupt)
				return null;

			if (ProjectDatabase.AssetbundleToExtraDataMap.TryGetValue(assetBundleName, out ModEditorData modSettings))
			{
				return modSettings;
			}

			return null;
		}


		/// <summary>
		/// If the Project Database does not exist or is corrupt,
		/// this attempts to reload it from disk, and returns false.
		/// </summary>
		/// <returns>
		/// True if the project database was already loaded or able to be loaded, false otherwise.
		/// </returns>
		public bool LoadProjectDatabaseIfNeeded(bool safely)
		{
			Logger.VerboseInfo("Load project database");
			if (ProjectDatabase == null || IsCorrupt)
			{
				if (lockdb)
				{
					return false;
				}

				Logger.VerboseInfo("Loading Project Database...");

				if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(ProjectDatabasePath)))
				{
					Logger.VerboseInfo("No Project Database in project, creating new Project Database...");

					ProjectDatabase = Editor.CreateInstance<ProjectDatabase>();

					AssetDatabase.CreateAsset(ProjectDatabase, ProjectDatabasePath);

					if (ProjectDatabase == null)
					{
						Logger.Error("Failed to create new Project Database!");

						IsCorrupt = false;

						return false;
					}

					ReimportAssets();
					IndexProjectDatabase();
					// Don't resave database as this might be called during the save step

					OnDatabaseLoad?.Invoke();
				}
				else
				{
					Logger.VerboseInfo("Found Project Database in project, loading in to memory...");

					try
					{
						ProjectDatabase = AssetDatabase.LoadAssetAtPath<ProjectDatabase>(ProjectDatabasePath);

						ProjectDatabase.DeserializeModsFromStorage();
					}
					catch (Exception e)
					{
						Logger.Error("Exception thrown when trying to load the Project Database:");
						Logger.Error(e);
					}

					if (ProjectDatabase == null)
					{
						if (safely)
						{
							Logger.VerboseError("Failed to load Project Database, it is likely corrupt!");

							IsCorrupt = true;

							OnCorruptionDetected?.Invoke();

							return false;
						}
						else
						{
							Logger.VerboseInfo("Load safety is disabled, creating new Project Database...");

							ProjectDatabase = Editor.CreateInstance<ProjectDatabase>();

							AssetDatabase.CreateAsset(ProjectDatabase, ProjectDatabasePath);

							if (ProjectDatabase == null)
							{
								Logger.Error("Failed to create new Project Database!");

								IsCorrupt = false;

								return false;
							}
						}
					}

					OnDatabaseLoad?.Invoke();
				}
			}

			IsCorrupt = false;

			return true;
		}

		~ProjectDatabaseManager()
		{
			AssetPostProcessListener.OnAssetPostProcessEvent -= AssetPostProcessCallback;
			SaveProjectDatabase();
		}

		/// <summary>
		/// Loads the project database if needed, serializes all Mod Metadata in it, and then marks the database as dirty.
		/// </summary>
		public void SaveProjectDatabase()
		{
			if (!LoadProjectDatabaseIfNeeded(true))
			{
				return;
			}

			ProjectDatabase.SerializeModsForStorage();

			EditorUtility.SetDirty(ProjectDatabase);

			Logger.VerboseInfo("Project Database marked as dirty");
		}

		/// <summary>
		/// Loads the project database if needed, reserializes the given mod, and then marks the database as dirty.
		/// </summary>
		public void SaveAssetBundle(string bundle)
		{
			if (!LoadProjectDatabaseIfNeeded(true))
			{
				return;
			}

			ProjectDatabase.SerializeModForStorage(bundle);
			EditorUtility.SetDirty(ProjectDatabase);

			Logger.VerboseInfo("Project Database marked as dirty");
		}

		/// <summary>
		/// Saves all changed assets,
		/// force reserializes (upgrades) all assets in project,
		/// and then reimports all changed files.
		/// <br/>
		/// This method is REALLY slow (>120 seconds on large projects), so only call it when you need to!]
		/// <br/>
		/// TODO: Part of this is probably the fact that all assets have a reimport callback placed on them... Also it's not parallelized.
		/// TODO: Can we make it only reimport the stuff in asset bundles? Do we even know what asset bundles exist?
		/// </summary>
		public void ReimportAssets()
		{
			if (lockdb)
			{
				return;
			}

			Logger.VerboseInfo("Saving Project Database...");

			AssetDatabase.SaveAssets();
			AssetDatabase.ForceReserializeAssets();
			AssetDatabase.Refresh();

			Logger.VerboseInfo("Done saving Project Database");
		}

		/// <summary>
		/// Iterates over all asset bundles in the project,
		/// and generates corresponding metadata for each asset and prefab in the corresponding AssetBundle.
		/// Removes any unused asset or prefab metadata.
		/// <br/>
		/// Does NOT save the database. Call SaveProjectDatabase() to ensure it is saved to disk as needed.
		/// <br/>
		/// TODO: Investigate why this gets called 6 times during a reimport.
		/// Is it because I have 6 asset bundles? Shouldn't we only need to call this once?
		/// </summary>
		public void IndexProjectDatabase()
		{
			if (lockdb)
			{
				return;
			}

			Logger.VerboseInfo("Indexing Project Database...");

			// Pull record of all known asset bundles into a list for pruning
			List<string> invalidAssetBundleNames = ProjectDatabase.AssetbundleToModMap.Keys.ToList();

			// Fetch all asset bundles in the project
			foreach (string assetBundleName in AssetDatabase.GetAllAssetBundleNames())
			{
				// First, get our Mod data for the corresponding Asset Bundle
				ModMetadata mod;

				// If we found the existing asset bundle in our database...
				if (ProjectDatabase.AssetbundleToModMap.TryGetValue(assetBundleName, out mod))
				{
					Logger.VerboseInfo($"Found AssetBundle \"{assetBundleName}\" in Project Database");
					invalidAssetBundleNames.Remove(assetBundleName); // ...mark mod as valid
				}
				else
				{
					Logger.VerboseInfo($"New AssetBundle: \"{assetBundleName}\", adding to Project Database");

					// Otherwise, create corresponding mod metadata
					mod = new ModMetadata()
					{
						Uuid = Guid.NewGuid(),
						BuildNumberMap = new(),
						Name = assetBundleName,
						Owner = "Envision Center", // TODO: change for OS release
						Attribution = "",
						Creators = new(),
						AssetMap = new(),
						PrefabMap = new(),
						LatestTestedVersion = "v0.8.11",
					};

					// Add to database
					ProjectDatabase.AssetbundleToModMap.Add(assetBundleName, mod);
				}

				// Ensure mod has version list
				if (mod.BuildNumberMap == null)
				{
					Logger.VerboseInfo($"AssetBundle \"{assetBundleName}\" has no build number map, creating new...");
					mod.BuildNumberMap = new();
				}

				// Provide sane defaults for build target versions
				foreach (BuildTarget target in CollabModdingWindow.SupportedTargets.Keys)
				{
					if (!mod.BuildNumberMap.ContainsKey(target.ToString()))
					{
						Logger.VerboseInfo($"AssetBundle \"{assetBundleName}\"'s build number map has no version for {target}, setting to zero...");
						mod.BuildNumberMap.Add(target.ToString(), 0);
					}
				}

				// Ensures mod has editor-only metadata
				ModEditorData modSettings;
				if (ProjectDatabase.AssetbundleToExtraDataMap.TryGetValue(assetBundleName, out modSettings))
				{
					Logger.VerboseInfo($"AssetBundle \"{assetBundleName}\" has extra data");
				}
				else
				{
					Logger.VerboseInfo($"AssetBundle \"{assetBundleName}\" has no extra data, creating new...");

					modSettings = new ModEditorData() { ExtraPrefabSettings = new() };
					ProjectDatabase.AssetbundleToExtraDataMap.Add(assetBundleName, modSettings);
				}

				// Create a list for pruning Assets and Prefabs
				List<Guid> invalidAssetUuids = mod.AssetMap.Keys.ToList();
				List<Guid> invalidPrefabUuids = mod.PrefabMap.Keys.ToList();

				// For every item in the Asset Bundle...
				foreach (string asset in AssetDatabase.GetAssetPathsFromAssetBundle(assetBundleName))
				{
					// Generate an arbitrary GUID for it (note: differs from Unity GUIDs since they must be used in-game)
					string databaseGuid = AssetDatabase.AssetPathToGUID(asset);
					Guid assetUuid = Guid.Parse(AssetDatabase.AssetPathToGUID(asset));

					// Add mod to asset map if it was not already there
					if (!mod.AssetMap.ContainsKey(assetUuid))
					{
						Logger.VerboseInfo($"Asset \"{asset}\" (from AssetBundle \"{assetBundleName}\") is not on asset map, adding...");
						mod.AssetMap.Add(assetUuid, asset);
						modSettings.ExtraAssetSettings.Add(assetUuid.ToString(), new ExtraAssetSettings());
					}

					Logger.VerboseInfo($"Found Asset \"{asset}\" (from AssetBundle \"{assetBundleName}\") in asset map");
					mod.AssetMap[assetUuid] = asset;

					invalidAssetUuids.Remove(assetUuid);

					if (!ProjectDatabase.AssetbundleToExtraDataMap[assetBundleName].ExtraAssetSettings.ContainsKey(assetUuid.ToString()))
					{
						Logger.VerboseInfo($"Asset \"{asset}\" (from AssetBundle \"{assetBundleName}\") has no extra data, creating new...");
						ProjectDatabase.AssetbundleToExtraDataMap[assetBundleName].ExtraAssetSettings.Add(assetUuid.ToString(), new ExtraAssetSettings());
					}
					else
					{
						Logger.VerboseInfo($"Asset \"{asset}\" (from AssetBundle \"{assetBundleName}\") has extra data");
					}

					// Only operate prefabs listed in the prefab map (i.e. marked as "Menu Item"), and are actually a prefab
					if (mod.PrefabMap.ContainsKey(assetUuid) && AssetDatabase.GetMainAssetTypeFromGUID(new GUID(databaseGuid)) == typeof(GameObject))
					{
						Logger.VerboseInfo($"Found Asset \"{asset}\" (from AssetBundle \"{assetBundleName}\") in prefab map");
						invalidPrefabUuids.Remove(assetUuid);

						// Ensure editor-only metadata exists
						if (!modSettings.ExtraPrefabSettings.ContainsKey(assetUuid.ToString()))
						{
							Logger.VerboseInfo($"Asset \"{asset}\" (from AssetBundle \"{assetBundleName}\") has no extra prefab data, creating new...");
							modSettings.ExtraPrefabSettings.Add(assetUuid.ToString(), new ExtraPrefabSettings());
						}
						else
						{
							Logger.VerboseInfo($"Asset \"{asset}\" (from AssetBundle \"{assetBundleName}\") has extra prefab data");
						}
					}
				}

				// Prune unused prefab data
				foreach (Guid invalidPrefabUuid in invalidPrefabUuids)
				{
					Logger.VerboseInfo($"Asset \"{mod.AssetMap[invalidPrefabUuid]}\" (from AssetBundle \"{assetBundleName}\") no longer exists, removing from prefab map...");
					mod.PrefabMap.Remove(invalidPrefabUuid);
					// ProjectDatabase.AssetbundleToExtraDataMap[assetBundleName].PrefabTextureSettings.Remove(invalidPrefabUuid.ToString());
				}

				// Prune unused asset data
				foreach (Guid invalidAssetUuid in invalidAssetUuids)
				{
					Logger.VerboseInfo($"Asset \"{mod.AssetMap[invalidAssetUuid]}\" (from AssetBundle \"{assetBundleName}\") no longer exists, removing from asset map...");
					mod.AssetMap.Remove(invalidAssetUuid);
				}
			}

			// Remove Asset Bundles that were not found from the database
			foreach (string invalidAssetBundleName in invalidAssetBundleNames)
			{
				Logger.VerboseInfo($"AssetBundle \"{invalidAssetBundleName}\" no longer exists, removing from Project Database...");
				ProjectDatabase.AssetbundleToModMap.Remove(invalidAssetBundleName);
				// ProjectDatabase.AssetbundleToExtraDataMap.Remove(invalidAssetBundleName);
			}
		}

		bool lockdb = false;

		public void LockProjectDatabase()
		{
			Logger.VerboseInfo("Project Database Locked");

			lockdb = true;
		}

		public void UnlockProjectDatabase()
		{
			Logger.VerboseInfo("Project Database Unlocked");

			lockdb = false;
		}

		void AssetPostProcessCallback(string[] imported_assets, string[] deleted_assets, string[] moved_assets, string[] moved_from_asset_paths)
		{
			if (ProjectDatabase == null || IsCorrupt)
				return;

			Logger.VerboseInfo("Project Database Post Processing...");

			IndexProjectDatabase();
			SaveProjectDatabase();

			Logger.VerboseInfo("Project Database Done Post Processing");

			OnPostAssetPostProcess?.Invoke();
		}
	}
}
