using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace CollabXR.ModPackager
{
	public class ModMetadata
	{
		[UnityEngine.Scripting.Preserve]
		public ModMetadata() { }

		public Guid Uuid;

		public Dictionary<string, int> BuildNumberMap;
		public string Name;
		public string Owner;
		public string Attribution; // used if want to auto-populate all prefab attributions in bundle
		public List<string> Creators;

		public Dictionary<Guid, string> AssetMap;
		public Dictionary<Guid, ModPrefab> PrefabMap;
		public Dictionary<Guid, ModScene> SceneMap;

		public string LatestTestedVersion;
	}

	public class ModPrefab // For Prefabs
	{
		[UnityEngine.Scripting.Preserve]
		public ModPrefab() { }

		public string Category = "";
		public string FormattedName = "";
		public string Attribution = "";

		public Vector3 MinScale = Vector3.one / 2;
		public Vector3 MaxScale = Vector3.one * 2;
		public Vector3 StartingOffset = Vector3.zero;

		[JsonConverter(typeof(Texture2DConverter))]
		public Texture2D Thumbnail = null;

		// Want additional metadata? You can add it! Just ask Hazel!
	}

	public class ModScene // For Scenes
	{
		[UnityEngine.Scripting.Preserve]
		public ModScene() { }

		public string Category = "";
		public string FormattedName = "";
		public string Attribution = "";
		
		/// <summary>
		/// Name -> Global Scene Position mapping for teleportation points. 
		/// </summary>
		public Dictionary<string, Vector3> teleports = new Dictionary<string, Vector3>();

		[JsonConverter(typeof(Texture2DConverter))]
		public Texture2D Thumbnail = null;

		public string AddBlankTeleport()
		{
			string newTeleportName = "New Teleport";
			int counter = 1;
			while (teleports.ContainsKey(newTeleportName))
			{
				newTeleportName = $"New Teleport {counter}";
				counter++;
			}

			teleports.Add(newTeleportName, Vector3.zero);

			return newTeleportName;
		}
	}

	public class Texture2DConverter : JsonConverter<Texture2D>
	{
		[UnityEngine.Scripting.Preserve]
		public Texture2DConverter() { }

		public override void WriteJson(JsonWriter writer, Texture2D value, JsonSerializer serializer)
		{
			if (value == null)
			{
				writer.WriteNull();
				return;
			}

			try
			{
				writer.WriteValue(Convert.ToBase64String(value.EncodeToPNG()));
			}
			catch (Exception)
			{
				Logger.Error("Couldn't serialize use your texture! Do you have compression enabled?");

				writer.WriteNull();
			}
		}

		/// <summary>
		/// Deserializes and returns the thumbnail from the JSON structure.
		/// </summary>
		/// <returns>
		/// The Prefab thumbnail. Returns null if invalid.
		/// </returns>
		public override Texture2D ReadJson(JsonReader reader, Type objectType, Texture2D existingValue, bool hasExistingValue, JsonSerializer serializer)
		{
			string sprite = (string)reader.Value;

			if (sprite == null)
			{
				return null;
			}

			try
			{
				Texture2D texture2D = new Texture2D(1, 1);
				texture2D.LoadImage(Convert.FromBase64String(sprite));

				return texture2D;
			}
			catch (Exception)
			{
				return null;
			}
		}
	}
}
