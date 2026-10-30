using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CollabXR.ModPackager.EnvironmentExtras
{
	[CreateAssetMenu(menuName = "CollabXR/Environment Data")]
	public class EnvironmentData : ScriptableObject
	{
		public string sceneName;

		[Tooltip("The path to the scene asset, auto-computed from the Scene Asset")]
		public string scenePath;
		public string sceneGuid;

#if UNITY_EDITOR
		[Tooltip("Editor only, the scene asset for this environment")]
		[SerializeField] SceneAsset sceneAsset;
#endif

		public EnvironmentTeleportInfo[] teleportInfo;
		public EmbeddedNetworkObjects networkObjects;

#if UNITY_EDITOR
		private void OnValidate()
		{
			// auto-populates scenePath and sceneName from the editor-only Scene Asset
			if (sceneAsset != null)
			{
				scenePath = AssetDatabase.GetAssetPath(sceneAsset);
				sceneName = sceneAsset.name;
				sceneGuid = AssetDatabase.AssetPathToGUID(scenePath);
			}
		}
#endif
	}

	[Serializable]
	public struct EnvironmentTeleport
	{
		public Transform transform;
		public EnvironmentTeleportInfo info;
	}

	[Serializable]
	public struct EnvironmentTeleportInfo
	{
		public string name;
		public Sprite thumbnail;
	}

	[Serializable]
	public struct EmbeddedNetworkObjects
	{
		public List<GameObject> objects;
	}
}
