using System;
using System.IO;
using UnityEngine;

namespace CollabXR.ModPackager
{
	/// <summary>
	/// Use to create scriptable objects for storing data and serialize them into json.
	/// <br></br>
	/// To Use:
	/// Create a class implementing <see cref="ScriptableData{T}"/> like <see cref="ScriptableString"/>
	/// and pass it into <see cref="CreateAndSaveData{T}(out ScriptableData{T}, T, bool, string, string)"/>
	/// </summary>
	public static class ScriptableDataCreator
	{
		public static readonly string defaultPath = Path.Combine(Application.persistentDataPath, "Collab_Mod_Packager");

		/// Note:
		/// Unity doesn't like serializing generic T, so please refer to <see cref="ScriptableData{T}"/>
		/// if you need to serialize something not yet supported.
		/// <summary>
		/// Create a new <see cref="ScriptableData{T}"/> with the option of writing it to json in local storage.
		/// </summary>
		/// <param name="scriptableData"> scriptable data to write into </param>
		/// <param name="data"> the data to be stored </param>
		/// <param name="saveToFile"> if the data needs to be serialized </param>
		/// <param name="fileName"> name of the resulting json file if saving </param>
		/// <param name="savePath"> defaults to Application.persistentDataPath </param>
		public static void CreateAndSaveData<T>(out ScriptableData<T> scriptableData, T data, bool saveToFile, string fileName = default, string savePath = default)
		{
			scriptableData = (ScriptableData<T>)ScriptableData<T>.GetScriptableObjectOfType(typeof(T));
			if (scriptableData == null)
			{
				Logger.VerboseError($"Failed to create data of type {typeof(T)}. Please ensure the corresponding scriptable type is created in ScriptableData");
				return;
			}
			scriptableData.data = data;

			if (saveToFile)
			{
				SaveData(scriptableData, fileName, savePath);
			}
		}

		/// <summary>
		/// Create a scriptable data by reading the given json file
		/// </summary>
		/// <param name="scriptableData"> scriptable data to write into </param>
		/// <param name="fileName"> name of the json file to read </param>
		/// <param name="savePath"> path to the json file </param>
		public static void CreateDataFromFile<T>(out ScriptableData<T> scriptableData, string fileName, string savePath)
		{
			scriptableData = (ScriptableData<T>)ScriptableData<T>.GetScriptableObjectOfType(typeof(T));
			if (!fileName.EndsWith(".json"))
			{
				fileName += ".json";
			}
			savePath = Path.Combine(savePath, fileName);

			try
			{
				if (File.Exists(savePath))
				{
					string json = File.ReadAllText(savePath);
					JsonUtility.FromJsonOverwrite(json, scriptableData);
				}
			}
			catch (Exception e)
			{
				scriptableData = null;
				Logger.VerboseError($"Error {e}: Failed to create scriptable data from path {savePath}");
			}
		}

		/// <summary>
		/// Saves the given <see cref="ScriptableData{T}"/> into local storage.
		/// </summary>
		/// <param name="fileName"> name of the resulting json file </param>
		/// <param name="savePath"> defaults to Application.persistentDataPath </param>
		public static void SaveData<T>(ScriptableData<T> scriptableData, string fileName, string savePath = default)
		{
			if (string.IsNullOrWhiteSpace(fileName))
			{
				Logger.VerboseError($"Failed to save ScriptableData: please provide a valid name for saving {scriptableData}");
				return;
			}

			if (string.IsNullOrWhiteSpace(savePath))
			{
				savePath = defaultPath;
			}

			try
			{
				if (!Directory.Exists(savePath))
				{
					Directory.CreateDirectory(savePath);
				}
				savePath = Path.Combine(savePath, fileName + ".json");

				File.WriteAllText(savePath, JsonUtility.ToJson(scriptableData));
			}
			catch (Exception e)
			{
				Logger.VerboseError($"Error {e}: Failed to serialize scriptable object {scriptableData}");
			}
		}
	}
}
