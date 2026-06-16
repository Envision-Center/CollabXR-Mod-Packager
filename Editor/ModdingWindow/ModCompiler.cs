using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace CollabXR.ModPackager
{
	public class ModCompiler
	{
		public ModCompiler() { }

		// void GenerateScriptReferenceCounter(string asset_bundle, string output_path)
		// {
		//     ScriptReferenceCounter new_reference_counter = ScriptableObject.CreateInstance<ScriptReferenceCounter>();

		//     string[] assets = AssetDatabase.GetAssetPathsFromAssetBundle(asset_bundle);

		//     foreach (string asset in assets)
		//     {
		//        //if (AssetDatabase.get)
		//     }

		//     AssetDatabase.CreateAsset(new_reference_counter, output_path);

		//     AssetDatabase.ImportAsset(output_path);

		//     SetAssetBundle(output_path, target_asset_bundle, "");

		//     AssetDatabase.Refresh();
		// }

		// void MoveAndTarget(string from_path, string to_path)
		// {
		//     AssetDatabase.ImportAsset(from_path);

		//     File.Move(from_path, to_path);

		//     AssetDatabase.ImportAsset(to_path);

		//     SetAssetBundle(to_path, target_asset_bundle, "");

		//     AssetDatabase.Refresh();
		// }

		public static void CompileMod(string targetAssetBundle, ModMetadata modMetadata, BuildTarget target)
		{
			// var scripts = new[] { "Assets/Scripts/TestObject.cs" };
			var build_folder = "Assets/Build";

			Logger.VerboseInfo("Serialising Initial Metadata...");

			string metadataJson = JsonConvert.SerializeObject(modMetadata, Formatting.None, new JsonSerializerSettings() { ReferenceLoopHandling = ReferenceLoopHandling.Ignore });

			// var raw_assembly_path = $"{build_folder}/tmp/{target_asset_bundle}-code.dll";
			// var processed_assembly_path = $"{build_folder}/tmp/{target_asset_bundle}-code.bytes";
			// var script_reference_counter_path = $"{build_folder}/tmp/{target_asset_bundle}-rc.asset";

			// GenerateScriptReferenceCounter(target_asset_bundle, script_reference_counter_path);

			// var assembly_builder = new AssemblyBuilder(raw_assembly_path, scripts);

			// assembly_builder.excludeReferences = new string[] { raw_assembly_path };

			// assembly_builder.buildFinished += delegate (string assembly_path, CompilerMessage[] compiler_messages)
			// {
			//     var errorCount = compiler_messages.Count(m => m.type == CompilerMessageType.Error);
			//     var warningCount = compiler_messages.Count(m => m.type == CompilerMessageType.Warning);

			//     if (errorCount == 0)
			//     {
			//         MoveAndTarget(raw_assembly_path, processed_assembly_path);

			//         AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
			//             "Assets/Build",
			//             BuildAssetBundleOptions.ForceRebuildAssetBundle,
			//             BuildTarget.Android
			//         );

			//         AssetDatabase.Refresh();

			//         AssetDatabase.DeleteAsset($"{build_folder}/tmp/");

			//         AssetDatabase.Refresh();
			//     }
			// };

			// assembly_builder.Build();

			Logger.VerboseInfo($"Creating Build Folder \"{build_folder}\"");

			if (!AssetDatabase.IsValidFolder($"{build_folder}"))
			{
				string createError = AssetDatabase.CreateFolder("Assets", "Build");

				if (createError == "")
					throw new Exception($"Failed to create Build folder");
			}

			Logger.VerboseInfo("Creating temporary artifact Folder...");

			if (!AssetDatabase.IsValidFolder($"{build_folder}/tmp"))
			{
				string createError = AssetDatabase.CreateFolder($"{build_folder}", "tmp");

				if (createError == "")
				{
					throw new Exception($"Failed to create tmp folder");
				}
			}

			Logger.VerboseInfo("Indexing AssetBundle...");

			string[] allAssetsInTargetAssetBundle = AssetDatabase.GetAssetPathsFromAssetBundle(targetAssetBundle);

			Logger.VerboseInfo("All assets:");

			foreach (string asset in allAssetsInTargetAssetBundle)
			{
				Logger.VerboseInfo($"  - {asset}");
			}

			Logger.VerboseInfo("Generating Build...");

			AssetBundleBuild[] assetBundleBuilds = new AssetBundleBuild[1];

			assetBundleBuilds[0].assetBundleName = targetAssetBundle;
			assetBundleBuilds[0].assetNames = allAssetsInTargetAssetBundle;

			Logger.VerboseInfo("Building...");

			AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
				$"{build_folder}/tmp",
				assetBundleBuilds,
				BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.ChunkBasedCompression,
				target
			);

			// According to the Unity docs, building the asset bundle may return null without an exception
			// https://docs.unity3d.com/6000.0/Documentation/ScriptReference/BuildPipeline.BuildAssetBundles.html
			if (manifest == null)
			{
				throw new Exception("built AssetBundle manifest was null, are there script compile errors?");
			}

			Logger.VerboseInfo(string.Format("Build manifest contained these AssetBundles: {0}", String.Join(",", manifest.GetAllAssetBundles())));

			string modMetadataPath = $"{build_folder}/{modMetadata.Uuid}.{target}";
			Logger.VerboseInfo($"Deleting old Metadata asset: \"{modMetadataPath}\"");
			AssetDatabase.DeleteAsset(modMetadataPath);

			AssetDatabase.Refresh();

			Logger.VerboseInfo($"Copying Build from \"{build_folder}/tmp/{targetAssetBundle}\" -> \"{modMetadataPath}\"");

			string error = AssetDatabase.MoveAsset($"{build_folder}/tmp/{targetAssetBundle}", modMetadataPath);

			if (error != "")
			{
				throw new Exception($"Failed to move Asset Bundle: {error}");
			}

			Logger.VerboseInfo("Writing Metadata...");

			using (StreamWriter outputFile = new StreamWriter(Path.Combine(Application.dataPath, $"Build/{modMetadata.Uuid}.meta.json")))
			{
				outputFile.WriteLine(metadataJson);
			}

			// AssetDatabase.Refresh();

			Logger.VerboseInfo("Cleaning up temporary artifacts...");

			AssetDatabase.DeleteAsset($"{build_folder}/tmp/");

			AssetDatabase.Refresh();

			Logger.VerboseInfo("Done!");
		}
	}
}
