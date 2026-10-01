using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace CollabXR.ModPackager
{
	public class RepositoryMetadata
	{
		[UnityEngine.Scripting.Preserve]
		public RepositoryMetadata() { }

		public int StructVersion;

		public string BaseURL;

		public string S3BucketName;

		public string CognitoURL;
		public string CognitoUserPool;
		public string CognitoIdentityPool;
		public string CognitoClientID;

		public string RepoName;
		public string RepoOwner;

		public string accessKey;
		public string secretKey;

		public string[] Mods;

		/// <summary>
		/// Stores the folder path in which the mod is stored, look up by mod guid
		/// To get full path to mod, use folder path + mod guid
		/// </summary>
		[NonSerialized]
		public Dictionary<Guid, string> rootFolderLookUp;

		[OnDeserialized]
		private void ConstructLookUpTable(StreamingContext context)
		{
			rootFolderLookUp = new();
			foreach (var url in Mods)
			{
				int delim = url.LastIndexOf('/');
				if (!rootFolderLookUp.TryAdd(new(url[(delim + 1)..]), url[..(delim + 1)]))
				{
					Logger.Error("Found duplicate mod when loading repository metadata. Only one mod will be indexed.");
				}
			}

			Mods = new string[rootFolderLookUp.Count];
			int i = 0;
			foreach (var mod in rootFolderLookUp.Keys)
			{
				Mods[i++] = rootFolderLookUp[mod] + mod;
			}
		}
	}
}
