using System.Collections.Generic;
using UnityEngine;

namespace CollabXR.ModPackager
{
	// This is an old class from the scripting system from the modloader
	// it was used to keep track of where scripts were used in assetbundles
	// this isn't used anymore
	public class ScriptReferenceCounter : ScriptableObject
	{
		public class ScriptAttribution
		{
			string target_prefab;
			string target_path;

			string target_script_name;
		}

		string target_assetbundle;

		List<ScriptAttribution> script_attributions;
	}
}
