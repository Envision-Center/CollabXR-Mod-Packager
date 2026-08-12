using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace CollabXR.ModPackager
{
	// This class manages AWS S3 authorization and file interaction
	public class S3Client
	{
		/// The target S3 bucket we want to interact with
		string bucketName;

		// The core S3 client we're abstracting
		private AmazonS3Client client;

		// The transfer utility we use to manage uploads to S3
		private TransferUtility transferUtility;

		// Create a client using info present in the repository metadata
		public S3Client(RepositoryMetadata repositoryMetadata)
		{
			// We want our initial client and transfer utility to be null so we can manage their lifetime by calling Initialize
			client = null;
			transferUtility = null;

			// We want the bucket name from the repository metadata
			bucketName = repositoryMetadata.S3BucketName;

			Logger.VerboseInfo($"S3 created, using bucket {bucketName}");
		}

		// Initialize the client using credentials provided by AWSAuth
		public void Initialize(AWSAuth auth)
		{
			Logger.VerboseInfo("S3 initialized");

			// We need to validate if we're correctly authenticated otherwise we could get
			// nasty errors from trying to initialize S3 with unauthenticated credentials
			if (auth.IsAuthenticated)
			{
				// Create the S3 client and transfer utility with the credentials and the target region that was used for auth
				client = new AmazonS3Client(auth.credentials, auth.targetRegionEndpoint);
				transferUtility = new TransferUtility(client);

				// Register the sign out handler so we're guaranteed to dispose the client as soon as the auth system calls
				// this minus and then plus is because there can be a duplicate handler registered if AWS auth is reused after not calling OnSignOut
				// we do this to check that the event was removed before adding it
				auth.OnSignOut -= OnSignOut;
				auth.OnSignOut += OnSignOut;
			}
		}

		// Dispose the internal S3 client and transfer utility to reset this class to a fresh state
		private void OnSignOut()
		{
			Logger.VerboseInfo("S3 signed out");

			// as an extra check, we make sure the transer utility exists before disposing it
			if (transferUtility != null)
			{
				// dispose the transfer utility and then mark it as disposed
				transferUtility.Dispose();
				transferUtility = null;
			}

			// as an extra check, we make sure the S3 client exists before disposing it
			if (client != null)
			{
				// dispose the S3 client and then mark it as disposed
				client.Dispose();
				client = null;
			}
		}

		// This uploads a mod file and metadata set present in the build directory to S3
		public async Task<bool> UploadModAsync(Guid modUuid, string uploadFolder, BuildTarget target, Action<int> progressCallback)
		{
			Logger.VerboseInfo($"Uploading {modUuid} for {target} to S3...");

			if (!string.IsNullOrEmpty(uploadFolder))
			{
				uploadFolder += "/";
			}

			// We initiate an upload targeting the actual built assetbundle to S3
			bool firstPart = await UploadFileToS3(
				$"{uploadFolder}{modUuid}.{target}",
				Path.Combine(Application.dataPath, $"Build/{modUuid}.{target}"),
				(progress) =>
				{
					// Check if S3 is reporting the upload as failed
					if (progress == -2)
					{
						// propagate that up the chain and end early
						progressCallback?.Invoke(-2);
						return;
					}

					// give progress indication for the first half of the upload
					progressCallback?.Invoke(progress / 2);
				}
			);

			// We initiate an upload targeting the metadata json file for the mod to S3
			bool secondPart = await UploadFileToS3(
				$"{uploadFolder}{modUuid}.meta.json",
				Path.Combine(Application.dataPath, $"Build/{modUuid}.meta.json"),
				(progress) =>
				{
					// Check if S3 is reporting the upload as failed
					if (progress == -2)
					{
						// propagate that up the chain and end early
						progressCallback?.Invoke(-2);
						return;
					}

					// give the progress indication for the second half of the upload
					progressCallback?.Invoke(progress / 2 + 50);
				}
			);

			// Check and return the success status as the result of the task
			bool succeeded = firstPart && secondPart;

			if (succeeded)
			{
				Logger.VerboseInfo($"Successfully uploaded all files for {modUuid} for {target} to S3.");
			}
			else
			{
				Logger.VerboseError($"Failed to upload all files for {modUuid} for {target} to S3.");
			}

			return succeeded;
		}

		// This abstracts wrangling the transfer utility to perform an upload to S3
		private async Task<bool> UploadFileToS3(string filename, string filepath, Action<int> progressCallback)
		{
			Logger.VerboseInfo($"Uploading {filename} from {filepath} to S3...");

			// We create an initial request with the bucket, file name, and path
			TransferUtilityUploadRequest request = new TransferUtilityUploadRequest
			{
				BucketName = bucketName,
				Key = filename,
				FilePath = filepath,
				ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256,
			};

			// We register a progress callback before the transfer starts to get events out
			request.UploadProgressEvent += (sender, e) =>
			{
				// No logic here, we just propagate up the chain
				progressCallback?.Invoke(e.PercentDone);
			};

			try
			{
				// We attempt an async upload using the request
				await transferUtility.UploadAsync(request);
			}
			catch (Exception e)
			{
				Logger.Error($"Failed to upload {filename} to S3, please try again later.");
				Logger.Error(e);

				// There are cases where S3 will kick out an exception, we indicate to the progress handler that an error was thrown
				progressCallback?.Invoke(-2);

				// and we return the task early with a false result
				return false;
			}

			Logger.VerboseInfo($"Successfully uploaded {filename} to S3.");

			// No error was thrown so we return a success result from the task
			return true;
		}

		// This deletes a mod and it's corresponding metadata from S3
		public async Task<bool> DeleteModAsync(Guid modUuid, Action<int> progressCallback)
		{
			// the total number of objects is the number of supported platforms, plus the metadata file
			float totalObjects = CollabModdingWindow.SupportedTargets.Count + 1;
			// we need to keep track of how many objects we've deleted
			float deletedObjects = 0;

			// first request we make is the one for the metadata so it's delisted before we remove builds
			DeleteObjectRequest request = new DeleteObjectRequest { BucketName = bucketName, Key = $"{modUuid}.meta.json" };

			DeleteObjectResponse response;

			try
			{
				response = await client.DeleteObjectAsync(request);

				deletedObjects++;
				progressCallback?.Invoke((int)Math.Floor((deletedObjects / totalObjects) * 100));
			}
			catch (Exception e)
			{
				Logger.Error($"Failed to delete mod {modUuid} from S3, please try again later.");
				Logger.Error(e);

				progressCallback?.Invoke(-2);

				return false;
			}

			foreach (BuildTarget target in CollabModdingWindow.SupportedTargets.Keys)
			{
				request = new DeleteObjectRequest { BucketName = bucketName, Key = $"{modUuid}.{target}" };

				try
				{
					response = await client.DeleteObjectAsync(request);
				}
				catch (Exception) { }

				deletedObjects++;
				progressCallback?.Invoke((int)Math.Floor((deletedObjects / totalObjects) * 100));
			}

			progressCallback?.Invoke(100);

			return true;
		}

		public async Task<Dictionary<Guid, (ModMetadata, Dictionary<BuildTarget, bool>)>> GetModMetadata(List<Guid> mods, List<string> modPaths, Action<int> progressCallback)
		{
			float totalObjects = (mods.Count * (CollabModdingWindow.SupportedTargets.Count + 2)) + 1;
			float retrievedObjects = 0;

			Dictionary<Guid, (ModMetadata, Dictionary<BuildTarget, bool>)> repositoryMods = new();

			ListObjectsV2Request request = new ListObjectsV2Request { BucketName = bucketName };

			ListObjectsV2Response response;

			try
			{
				response = await client.ListObjectsV2Async(request);
			}
			catch (Exception e)
			{
				Logger.Error("Failed to list objects on S3, please try again later.");
				Logger.Error(e);

				progressCallback?.Invoke(-2);

				return null;
			}

			if (response.HttpStatusCode != System.Net.HttpStatusCode.OK)
			{
				Logger.Error("Failed to list objects on S3, please try again later.");

				progressCallback?.Invoke(-2);

				return null;
			}

			retrievedObjects++;
			progressCallback?.Invoke((int)Math.Floor((retrievedObjects / totalObjects) * 100));

			List<string> validObjects = response.S3Objects.Select(s3object => s3object.Key).ToList();

			for (int i = 0; i < modPaths.Count; i++)
			{
				Guid mod = mods[i];
				string modPath = modPaths[i];

				if (validObjects.Contains($"{modPath}.meta.json"))
				{
					Logger.VerboseInfo($"Getting Metadata for Mod {mod} from S3...");

					GetObjectResponse objectResponse;

					try
					{
						objectResponse = await client.GetObjectAsync(bucketName, $"{modPath}.meta.json");
					}
					catch (Exception e)
					{
						Logger.VerboseError($"Failed to get metadata for {modPath}");
						Logger.VerboseError(e);

						retrievedObjects += 2 + CollabModdingWindow.SupportedTargets.Count;
						progressCallback?.Invoke((int)Math.Floor((retrievedObjects / totalObjects) * 100));

						continue;
					}

					if (objectResponse.HttpStatusCode != System.Net.HttpStatusCode.OK)
					{
						Logger.VerboseError($"Failed to get metadata for {modPath}");

						retrievedObjects += 2 + CollabModdingWindow.SupportedTargets.Count;
						progressCallback?.Invoke((int)Math.Floor((retrievedObjects / totalObjects) * 100));

						continue;
					}

					StreamReader metadataReader = new StreamReader(objectResponse.ResponseStream);

					string metadataJson = await metadataReader.ReadToEndAsync();

					retrievedObjects++;
					progressCallback?.Invoke((int)Math.Floor((retrievedObjects / totalObjects) * 100));

					Logger.VerboseInfo($"Metadata for Mod {mod}: {metadataJson}");

					ModMetadata metadata;

					try
					{
						metadata = JsonConvert.DeserializeObject<ModMetadata>(metadataJson);
					}
					catch (Exception e)
					{
						Logger.VerboseError($"Failed to deserialize metadata for {modPath}");
						Logger.VerboseError(e);

						retrievedObjects += 1 + CollabModdingWindow.SupportedTargets.Count;
						progressCallback?.Invoke((int)Math.Floor((retrievedObjects / totalObjects) * 100));

						continue;
					}

					retrievedObjects++;
					progressCallback?.Invoke((int)Math.Floor((retrievedObjects / totalObjects) * 100));

					Dictionary<BuildTarget, bool> buildTargets = new();

					foreach (BuildTarget target in CollabModdingWindow.SupportedTargets.Keys)
					{
						buildTargets.Add(target, validObjects.Contains($"{modPath}.{target}"));

						retrievedObjects++;
						progressCallback?.Invoke((int)Math.Floor((retrievedObjects / totalObjects) * 100));
					}

					repositoryMods.Add(mod, (metadata, buildTargets));
				}
				else
				{
					Logger.VerboseInfo($"Mod {modPath} doesn't exist on S3");
				}
			}

			return repositoryMods;
		}

		public async Task<bool> DownloadObjectTo(string name)
		{
			GetObjectResponse objectResponse;

			try
			{
				objectResponse = await client.GetObjectAsync(bucketName, name);
			}
			catch (Exception e)
			{
				Logger.VerboseError($"Failed to download {name} from S3");
				Logger.VerboseError(e);

				return false;
			}

			if (objectResponse.HttpStatusCode != System.Net.HttpStatusCode.OK)
			{
				Logger.VerboseError($"Failed to download {name} from S3");

				return false;
			}

			return true;
		}
	}
}
