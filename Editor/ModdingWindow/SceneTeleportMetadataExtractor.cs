using System;
using System.Collections.Generic;
using CollabXR.ModPackager.EnvironmentExtras;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CollabXR.ModPackager
{
	/// <summary>
	/// Extracts teleportation point metadata from scenes in the mod.
	/// Only use during the mod packaging process to extract teleport data from the EnvironmentScene script in scenes.
	/// </summary>
	internal static class SceneTeleportMetadataExtractor
	{
		/// <summary>
		/// Given a mod metadata, iterate all scenes in the mod metadata and build a dictionary
		/// mapping scene UUIDs to their respective list of teleport names.
		/// </summary>
		/// <param name="metadata"></param>
		/// <returns></returns>
		/// <exception cref="InvalidOperationException"></exception>
		public static Dictionary<Guid, List<string>> Extract(ModMetadata metadata)
		{
			if (metadata?.SceneMap == null || metadata.AssetMap == null)
			{
				throw new InvalidOperationException("Scene metadata is unavailable.");				
			}

			Dictionary<Guid, List<string>> sceneTeleports = new();
			foreach (Guid sceneUuid in metadata.SceneMap.Keys)
			{
				// check for valid path and that the asset at path is a scene
				if (!metadata.AssetMap.TryGetValue(sceneUuid, out string scenePath) ||
					string.IsNullOrEmpty(scenePath) ||
					AssetDatabase.GetMainAssetTypeAtPath(scenePath) != typeof(SceneAsset))
				{
					throw new InvalidOperationException($"Scene {sceneUuid} has no valid scene asset path.");
				}
				// extract teleports and add to dict
				sceneTeleports.Add(sceneUuid, ExtractFromScene(scenePath));
			}

			return sceneTeleports;
		}

		/// <summary>
		/// Extract teleports from a given scene path.
		/// 1. Actually opens the scene (if not already loaded).
		/// 2. Finds the EnvironmentScene script in the scene (ensures their is EXACTLY ONE PRESENT).
		/// 3. Adds the names of the teleports to a list and returns it.
		/// NOTE: This operation can fail for many reasons.
		/// </summary>
		/// <param name="scenePath"></param>
		/// <returns></returns>
		/// <exception cref="InvalidOperationException"></exception>
		private static List<string> ExtractFromScene(string scenePath)
		{
			Scene scene = SceneManager.GetSceneByPath(scenePath);
			bool openedForExtraction = !scene.isLoaded;

			if (openedForExtraction)
			{
				scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);				
			}

			try
			{
				if (scene.isDirty)
				{
					throw new InvalidOperationException($"Save changes to '{scenePath}' before building.");					
				}

				List<EnvironmentScene> environments = new();
				foreach (GameObject root in scene.GetRootGameObjects())
				{
					environments.AddRange(root.GetComponentsInChildren<EnvironmentScene>(true));					
				}

				if (environments.Count != 1)
				{
					throw new InvalidOperationException(
						$"Scene '{scenePath}' must contain exactly one EnvironmentScene component; found {environments.Count}.");
				}

				List<string> names = new();
				HashSet<string> seenNames = new(StringComparer.Ordinal); // used to track duplicates
				EnvironmentTeleport[] teleports = environments[0].teleports;
				if (teleports == null)
				{
					return names;					
				}

				for (int index = 0; index < teleports.Length; index++)
				{
					string name = teleports[index].info.name;
					if (string.IsNullOrWhiteSpace(name))
					{
						throw new InvalidOperationException($"Teleport {index + 1} in '{scenePath}' has no name.");
					}
					if (!seenNames.Add(name)) // duplicate name found
					{
						throw new InvalidOperationException($"Scene '{scenePath}' has duplicate teleport name '{name}'.");						
					}

					names.Add(name);
				}

				return names;
			}
			finally
			{
				if (openedForExtraction)
				{
					EditorSceneManager.CloseScene(scene, true);					
				}
			}
		}
	}
}
