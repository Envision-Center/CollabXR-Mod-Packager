using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Android.Gradle;
using UnityEditor;
using UnityEngine;

namespace CollabXR.ModPackager
{
	public class ModPackagerRepositoryManager
	{
		static readonly HttpClient client = new HttpClient();

		public string TargetRepository => targetRepository.data;
		public ScriptableData<string> targetRepository;
		private string savedRepoDir = ScriptableDataCreator.defaultPath;

		public RepositoryMetadata repositoryMetadata { get; private set; }

		public Dictionary<Guid, (ModMetadata, Dictionary<BuildTarget, bool>)> repositoryMods = null;

		public delegate void OnRepositoryLoadedEvent();
		public event OnRepositoryLoadedEvent OnRepositoryLoaded;

		public delegate void OnRepositoryLoadFailedEvent();
		public event OnRepositoryLoadFailedEvent OnRepositoryLoadFailed;

		public delegate void OnRepositoryIndexProgressEvent(int indexProgress);
		public event OnRepositoryIndexProgressEvent OnRepositoryIndexProgress;

		public delegate void OnRepositoryIndexedEvent();
		public event OnRepositoryIndexedEvent OnRepositoryIndexed;

		public delegate void OnRepositoryIndexFailedEvent();
		public event OnRepositoryIndexFailedEvent OnRepositoryIndexFailed;

		AWSAuth awsAuth;
		S3Client s3Client;

		Task uploadLoopTask;

		public bool IsAuthenticated
		{
			get
			{
				if (repositoryMetadata == null)
					return false;

				if (awsAuth == null)
					return false;

				return awsAuth.IsAuthenticated;
			}
		}

		List<(ModMetadata, BuildTarget, int)> pendingUploads = new();

		public delegate void UploadQueueUpdateEvent(List<(ModMetadata, BuildTarget, int)> uploads);
		public event UploadQueueUpdateEvent OnUploadQueueUpdate;

		public delegate void OnDeleteProgressEvent(Guid mod, int deleteProgress);
		public event OnDeleteProgressEvent OnDeleteProgress;

		public delegate void OnSignInSuccessEvent();
		public event OnSignInSuccessEvent OnSignInSuccess;

		public delegate void SignInFailedEvent();
		public event SignInFailedEvent OnSignInFailed;

		public delegate void SignOutEvent();
		public event SignOutEvent OnSignOut;

		public delegate string NewPasswordChallengeEvent();
		public event NewPasswordChallengeEvent OnNewPasswordChallenge;

		public delegate string MFAChallengeEvent();
		public event MFAChallengeEvent OnMFAChallenge;

		public ModPackagerRepositoryManager()
		{
			OnRepositoryLoaded += SaveRepoOnLoad;

			ScriptableDataCreator.CreateDataFromFile(out targetRepository, "lastVisitedRepo", savedRepoDir);

			if (!string.IsNullOrWhiteSpace(TargetRepository))
			{
				SwitchRepository(TargetRepository);
			}
		}

		void initAWS()
		{
			Logger.VerboseInfo("Initialising AWS...");

			if (repositoryMetadata == null)
				return;

			awsAuth = new AWSAuth(repositoryMetadata);
			s3Client = new S3Client(repositoryMetadata);

			awsAuth.OnSignInSuccess += () =>
			{
				Logger.VerboseInfo("Sign in successful!");

				OnSignInSuccess?.Invoke();
			};
			awsAuth.OnSignInFailed += () =>
			{
				Logger.VerboseInfo("Sign in failed!");

				OnSignInFailed?.Invoke();
			};
			awsAuth.OnSignOut += () =>
			{
				Logger.VerboseInfo("Sign out successful!");

				OnSignOut?.Invoke();
			};
			awsAuth.OnMFAChallenge += () =>
			{
				Logger.VerboseInfo("MFA challenge issued!");

				return OnMFAChallenge?.Invoke();
			};
			awsAuth.OnNewPasswordChallenge += () =>
			{
				Logger.VerboseInfo("Password change issued!");

				return OnNewPasswordChallenge?.Invoke();
			};

			uploadLoopTask = Task.Run(async () =>
			{
				while (awsAuth != null)
				{
					while (IsAuthenticated && pendingUploads.Count > 0)
					{
						Logger.VerboseInfo("Upload loop has an upload");

						(ModMetadata, BuildTarget, int) nextUpload = pendingUploads[0];


						_ = await s3Client.UploadModAsync(
							nextUpload.Item1.Uuid,
							nextUpload.Item1.FolderPath,
							nextUpload.Item2,
							(progress) =>
							{
								nextUpload.Item3 = progress;
								pendingUploads[0] = nextUpload;
								OnUploadQueueUpdate?.Invoke(pendingUploads);
							}
						);

						pendingUploads.RemoveAt(0);
						OnUploadQueueUpdate?.Invoke(pendingUploads);

						IndexMods();

						Logger.VerboseInfo("Upload loop completed upload");
					}

					await Task.Delay(500);
				}

				Logger.VerboseInfo("Gone, goodbye upload loop");
			});
		}

		~ModPackagerRepositoryManager()
		{
			s3Client = null;
			awsAuth = null;

			uploadLoopTask.Dispose();
		}

		public void SignIn(string username, string password)
		{
			Logger.VerboseInfo("Signing in...");

			Task.Run(async () =>
			{
				await awsAuth.SignIn(username, password);

				s3Client.Initialize(awsAuth);

				if (IsAuthenticated)
					IndexMods();

				Logger.VerboseInfo("Done signing in");
			});
		}

		public void SignOut()
		{
			Logger.VerboseInfo("Signed out");

			if (awsAuth == null)
			{
				OnSignOut?.Invoke();
				return;
			}

			awsAuth.SignOut();
		}

		public void UploadMod(ModMetadata modMetaData, BuildTarget buildTarget)
		{
			Logging.LogInfo($"Mod {modMetaData.Uuid} built for {buildTarget} queued for upload...");

			pendingUploads.Add((modMetaData, buildTarget, -1));
			OnUploadQueueUpdate?.Invoke(pendingUploads);
		}

		public void DeleteMod(Guid modUuid)
		{
			Logger.VerboseInfo($"Deleting Mod {modUuid}...");

			Task.Run(async () =>
			{
				OnDeleteProgress?.Invoke(modUuid, -1);

				bool success = await s3Client.DeleteModAsync(
					modUuid,
					(progress) =>
					{
						OnDeleteProgress?.Invoke(modUuid, progress);
					}
				);

				IndexMods();
			});
		}

		public void SwitchRepository(string repositoryUrl)
		{
			Logger.VerboseInfo("Repository switch triggered...");

			SignOut();

			s3Client = null;
			awsAuth = null;

			repositoryMetadata = null;
			repositoryMods = null;

			UpdateLastUsedRepo(repositoryUrl, false);

			Task.Run(async () =>
			{
				bool success = await refreshRepository();

				if (success)
				{
					initAWS();

					OnRepositoryLoaded?.Invoke();
				}
				else
				{
					OnRepositoryLoadFailed?.Invoke();
				}
			});
		}

		async Task<bool> refreshRepository()
		{
			Logger.VerboseInfo("Refreshing Repository...");

			try
			{
				using HttpResponseMessage response = await client.GetAsync(TargetRepository);

				response.EnsureSuccessStatusCode();

				string responseBody = await response.Content.ReadAsStringAsync();

				Logger.VerboseInfo($"Got Metadata for Repository: {responseBody}");

				repositoryMetadata = JsonConvert.DeserializeObject<RepositoryMetadata>(responseBody);

				return true;
			}
			catch (Exception e)
			{
				Logger.Error("Exception during Repository Load:");
				Logger.Error(e);

				return false;
			}
		}

		public void RefreshRepository()
		{
			Logger.VerboseInfo("In-place refresh triggered...");

			Task.Run(async () =>
			{
				bool success = await refreshRepository();

				if (success)
				{
					OnRepositoryLoaded?.Invoke();
				}
				else
				{
					OnRepositoryLoadFailed?.Invoke();
				}
			});
		}

		public void IndexMods()
		{
			Logger.VerboseInfo("Mod index triggered...");

			if (repositoryMetadata == null)
				return;

			Task.Run(async () =>
			{
				OnRepositoryIndexProgress?.Invoke(-1);

				await refreshRepository();

				OnRepositoryIndexProgress?.Invoke(50);

				repositoryMods = await s3Client.GetModMetadata(
					repositoryMetadata.rootFolderLookUp.Keys.ToList(), repositoryMetadata.Mods.ToList(),
					(progress) =>
					{
						if (progress == -2)
						{
							OnRepositoryIndexProgress?.Invoke(-2);
						}
						else
						{
							OnRepositoryIndexProgress?.Invoke((progress / 2) + 50);
						}
					}
				);

				if (repositoryMods == null)
				{
					OnRepositoryIndexFailed?.Invoke();
				}
				else
				{
					OnRepositoryIndexed?.Invoke();
				}
			});
		}

		private void SaveRepoOnLoad()
		{
			ScriptableDataCreator.SaveData(targetRepository, "lastVisitedRepo", ScriptableDataCreator.defaultPath);
		}

		private void UpdateLastUsedRepo(string repositoryUrl, bool saveToFile)
		{
			ScriptableDataCreator.CreateAndSaveData(out targetRepository, repositoryUrl, saveToFile, "lastVisitedRepo", ScriptableDataCreator.defaultPath);
		}
	}
}
