using UnityEditor;

namespace CollabXR.ModPackager
{
	public class AssetPostProcessListener : AssetPostprocessor
	{
		public delegate void AssetPostProcessEvent(string[] imported_assets, string[] deleted_assets, string[] moved_assets, string[] moved_from_asset_paths);
		public static event AssetPostProcessEvent OnAssetPostProcessEvent;

		static void OnPostprocessAllAssets(string[] imported_assets, string[] deleted_assets, string[] moved_assets, string[] moved_from_asset_paths)
		{
			Logger.VerboseInfo("Asset PostProcess called");

			if (OnAssetPostProcessEvent != null)
			{
				OnAssetPostProcessEvent.Invoke(imported_assets, deleted_assets, moved_assets, moved_from_asset_paths);
			}
		}
	}
}
