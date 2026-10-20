using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace CollabXR.ModPackager
{
	/// <summary>
	/// Unexported data that is only used for during the mod build step.
	/// </summary>
	[Serializable]
	public class ModEditorData
	{
		[SerializeField]
		public SerializableDictionary<string, ExtraAssetSettings> ExtraAssetSettings = new();

		[SerializeField]
		public SerializableDictionary<string, ExtraPrefabSettings> ExtraPrefabSettings = new();

		[SerializeField]
		public SerializableDictionary<string, ExtraSceneSettings> ExtraSceneSettings = new();
	}

	/// <summary>
	/// Settings tied specifically to assets.
	/// This is a class so it can be passed by reference.
	/// </summary>
	[Serializable]
	public class ExtraAssetSettings { }

	/// <summary>
	/// Settings tied specifically to prefabs.
	/// This is a class so it can be passed by reference.
	/// </summary>
	[Serializable]
	public class ExtraPrefabSettings
	{
		/// <summary>
		/// Whether to auto-generate the thumbnail for the Prefab.
		/// </summary>
		[SerializeField]
		public bool AutoGenerate = true;

		/// <summary>
		/// Optional thumbnail texture for the Prefab.
		/// This must be uncompressed in order to serialize!
		/// </summary>
		[SerializeField]
		public Texture2D Texture = null;
	}

	/// <summary>
	/// Settings tied specifically to scenes.
	/// This is a class so it can be passed by reference.
	/// </summary>
	[Serializable]
	public class ExtraSceneSettings
	{
		/// <summary>
		/// Mandatory thumbnail texture for the Scene.
		/// This must be uncompressed in order to serialize!
		/// </summary>
		[SerializeField]
		public Texture2D Texture = null;
	}

	/// <summary>
	/// Serialized database of mods within the Unity project.
	/// Contains mod metadata for each corresponding asset bundle in the project,
	/// and additional "extra" data that is only for the editor.
	/// </summary>
	public class ProjectDatabase : ScriptableObject
	{
		/// <summary>
		/// Whether to enable debug logging for the Mod Packager.
		/// </summary>
		[SerializeField]
		public bool VerboseLogging = false;

		/// <summary>
		/// Dictionary keyed by AssetBundle name, pointing to its corresponding ModMetadata.
		/// </summary>
		public Dictionary<string, ModMetadata> AssetbundleToModMap = new();

		/// <summary>
		/// Additional unexported/editor-only data tied to mods.
		/// </summary>
		[SerializeField]
		public SerializableDictionary<string, ModEditorData> AssetbundleToExtraDataMap = new();

		/// <summary>
		/// The AssetBundleToModMap, but serialized as JSON for storing.
		/// </summary>
		[SerializeField]
		SerializableDictionary<string, string> SerializableAssetbundleToModMap = new();

		/// <summary>
		/// Deserializes mod metadata from JSON strings that were stored on disk.
		/// </summary>
		public void DeserializeModsFromStorage()
		{
			if (AssetbundleToModMap == null)
			{
				AssetbundleToModMap = new();
			}
			if (AssetbundleToExtraDataMap == null)
			{
				AssetbundleToExtraDataMap = new();
			}

			AssetbundleToModMap.Clear();

			if (SerializableAssetbundleToModMap == null)
			{
				return;
			}

			foreach (string assetbundleName in SerializableAssetbundleToModMap.Keys)
			{
				AssetbundleToModMap.Add(assetbundleName, JsonConvert.DeserializeObject<ModMetadata>(SerializableAssetbundleToModMap[assetbundleName]));
			}
		}

		/// <summary>
		/// Serializes ALL existing mods into JSON strings for storing on disk.
		/// </summary>
		public void SerializeModsForStorage()
		{
			if (AssetbundleToModMap == null)
			{
				Debug.LogError("Not AssetBundle to ModMap!");
				return;
			}

			// If we did not have a dictionary to store into, create one.
			if (SerializableAssetbundleToModMap == null)
			{
				SerializableAssetbundleToModMap = new SerializableDictionary<string, string>();
			}
			else
			{
				// Otherwise, just clear the map so we keep its reference and don't allocate another object
				SerializableAssetbundleToModMap.Clear();
			}

			// Serialize Mod Metadata into JSON
			foreach (string bundle in AssetbundleToModMap.Keys)
			{
				SerializeModForStorage(bundle);
			}
		}

		/// <summary>
		/// Serializes a single mode for storage.
		/// </summary>
		public void SerializeModForStorage(string bundleName)
		{
			if (SerializableAssetbundleToModMap == null)
			{
				SerializableAssetbundleToModMap = new SerializableDictionary<string, string>();
			}

			// Remove key as necessary
			if (SerializableAssetbundleToModMap.ContainsKey(bundleName))
			{
				SerializableAssetbundleToModMap.Remove(bundleName);
			}

			SerializableAssetbundleToModMap.Add(
				bundleName,
				JsonConvert.SerializeObject(AssetbundleToModMap[bundleName], Formatting.None, new JsonSerializerSettings() { ReferenceLoopHandling = ReferenceLoopHandling.Ignore })
			);
		}
	}
}
