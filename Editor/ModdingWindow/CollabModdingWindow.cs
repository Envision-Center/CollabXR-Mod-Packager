using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FuzzySharp;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CollabXR.ModPackager
{
	public class CollabModdingWindow : EditorWindow
	{
		public const string BundleID = "edu.envision.collabmodpackager";
		public static Dictionary<BuildTarget, (BuildTargetGroup, string)> SupportedTargets = new()
		{
			{ BuildTarget.StandaloneWindows64, (BuildTargetGroup.Standalone, "Windows") },
			{ BuildTarget.StandaloneOSX, (BuildTargetGroup.Standalone, "MacOS") },
			{ BuildTarget.Android, (BuildTargetGroup.Android, "Quest") },
			{ BuildTarget.VisionOS, (BuildTargetGroup.VisionOS, "Vision Pro") },
		};

		VisualElement UIRootVisualElement;
		string targetAssetBundle = null;

		public ProjectDatabaseManager projectDatabaseManager = null;

		internal static List<Action> QueuedActions = new();

		[MenuItem("CollabXR Modding Tools/Open Mod Packager")]
		public static void ShowEditor()
		{
			Logger.VerboseInfo("Showing Mod Packager...");

			EditorWindow editorWindow = GetWindow<CollabModdingWindow>();

			editorWindow.titleContent = new GUIContent("CollabXR Mod Packager");
		}

		void Update()
		{
			while (QueuedActions.Count != 0)
			{
				Logger.VerboseInfo("UI Action!");

				try
				{
					QueuedActions[0]();
				}
				catch (Exception e)
				{
					Logger.VerboseError("Error during UI Action:");
					Logger.VerboseError(e);
				}
				QueuedActions.RemoveAt(0);
			}
		}

		void SetVisible(VisualElement element, bool visible)
		{
			if (visible)
			{
				element.style.display = DisplayStyle.Flex;
			}
			else
			{
				element.style.display = DisplayStyle.None;
			}
		}

		/// <summary>
		/// Preprocesses the mod metadata for a given target platform, generating thumbnails for prefabs if needed.
		/// </summary>
		/// <param name="target">The name of the target asset bundle.</param>
		/// <param name="metadata">The mod metadata to preprocess.</param>
		/// <returns>The preprocessed mod metadata.</returns>
		/// <details>
		/// Currently only generates thumbnails for prefabs that have the "AutoGenerate" flag set in the extra prefab settings for the target platform.
		/// Besides thumbnail stuff, this function is a no-op and just returns a copy of the metadata.
		/// </details>
		ModMetadata PreprocessMetadata(string target, ModMetadata metadata)
		{
			Logger.VerboseInfo($"Preprocessing Mod Metadata for {target}...");

			string metadataString = JsonConvert.SerializeObject(metadata, Formatting.None, new JsonSerializerSettings() { ReferenceLoopHandling = ReferenceLoopHandling.Ignore });

			ModMetadata metadataCopy = JsonConvert.DeserializeObject<ModMetadata>(metadataString);

			ModEditorData extraData = projectDatabaseManager.TryGetModEditorData(target);
			if (extraData != null)
			{
				/// This block checks if the mod has any extra prefab settings for the target platform
				/// if so, generates thumbnails for those prefabs if they are set to auto-generate.
				/// Scene thumbnails are not auto-generated, so don't care about them here.
				foreach (Guid prefabUuid in metadataCopy.PrefabMap.Keys)
				{
					string uuidStr = prefabUuid.ToString();
					if (extraData.ExtraPrefabSettings.Contains(uuidStr))
					{
						if (extraData.ExtraPrefabSettings[uuidStr].AutoGenerate)
						{
							string assetPath = metadata.AssetMap[prefabUuid];

							Logger.VerboseInfo($"Generating Thumbnail for {assetPath}...");

							UnityEngine.Object assetPrefab = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
							Editor newEditor = Editor.CreateEditor(assetPrefab);
							Texture2D previewTexture = newEditor.RenderStaticPreview(assetPath, null, 64, 64);
							EditorWindow.DestroyImmediate(newEditor);

							metadataCopy.PrefabMap[prefabUuid].Thumbnail = previewTexture;
						}
						else
						{
							metadataCopy.PrefabMap[prefabUuid].Thumbnail = extraData.ExtraPrefabSettings[uuidStr].Texture;
						}
					}
				}
			}

			Logger.VerboseInfo("Mod Metadata Preprocessing Done!");

			return metadataCopy;
		}

		void CompileMod(List<BuildTarget> targets, bool publish)
		{
			Logger.VerboseInfo($"Building{(publish ? " and Publishing" : "")} Mod for {string.Join(", ", targets.Select(target => target.ToString()))}");

			AssetDatabase.Refresh();

			Logger.VerboseInfo("Incrementing Build Numbers...");

			foreach (BuildTarget target in targets)
			{
				projectDatabaseManager.TryGetModMetadata(targetAssetBundle).BuildNumberMap[target.ToString()]++;
			}

			projectDatabaseManager.ReimportAssets();
			projectDatabaseManager.SaveProjectDatabase();

			Logger.VerboseInfo("Refreshing UI...");

			ReloadAssetBundleUI(targetAssetBundle);

			projectDatabaseManager.LockProjectDatabase();

			foreach (BuildTarget target in targets)
			{
				try
				{
					Logger.VerboseInfo($"Switching Active Build Target to {target}...");

					if (EditorUserBuildSettings.SwitchActiveBuildTarget(SupportedTargets[target].Item1, target))
					{
						// UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation(UnityEditor.Compilation.RequestScriptCompilationOptions.CleanBuildCache);

						Logger.VerboseInfo($"Compiling Mod for {target}...");

						ModCompiler.CompileMod(
							targetAssetBundle,
							PreprocessMetadata(targetAssetBundle, projectDatabaseManager.TryGetModMetadata(targetAssetBundle)),
							target
						);

						if (publish)
						{
							Logger.VerboseInfo("Adding Mod to Upload Queue...");
							repositoryManager.UploadMod(projectDatabaseManager.TryGetModMetadata(targetAssetBundle).Uuid, target);
						}
					}
					else
					{
						Logger.Error($"Failed to switch build target to {target}, please try again later.");
					}
				}
				catch (Exception e)
				{
					Logger.Error($"Exception during Mod Compilation for {target}:");
					Logger.Error(e);
				}
			}

			projectDatabaseManager.UnlockProjectDatabase();
		}

		void CreateGUI()
		{
			Logger.VerboseInfo("Creating GUI");

			Logger.VerboseInfo("Loading Main Visual Tree...");

			VisualTreeAsset mainUI = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"Packages/{CollabModdingWindow.BundleID}/Editor/ModdingWindow/ModdingWindow.uxml");

			Logger.VerboseInfo("Creating Root Visual Element...");

			UIRootVisualElement = mainUI.Instantiate();
			UIRootVisualElement.style.flexGrow = 1;

			Logger.VerboseInfo("Initialising UI...");

			InitializeUI();

			Logger.VerboseInfo("Presenting Root Visual Element...");

			rootVisualElement.Add(UIRootVisualElement);

			Logger.VerboseInfo("Done Creating GUI");
		}

		void OnDestroy()
		{
			projectDatabaseManager = null;
			repositoryManager = null;
		}

		void InitializeUI()
		{
			InitializeSettingsSection();

			InitializeProjectDatabase();

			InitializeTabview();

			InitializeAuthentication();

			InitializeAuthenticationSection();

			InitializeCompileModSection();

			InitializeModSetupSection();

			InitializeUploadQueueSection();
		}

		Box settingsBox;
		Toggle verboseLogging;
		EventCallback<ChangeEvent<bool>> verboseLoggingCallback;

		void InitializeSettingsSection()
		{
			settingsBox = UIRootVisualElement.Q<Box>("settings-box");

			verboseLogging = settingsBox.Q<Toggle>("verbose-logging");
			if (verboseLoggingCallback != null)
				verboseLogging.UnregisterValueChangedCallback(verboseLoggingCallback);
			verboseLoggingCallback = (e) =>
			{
				if (!projectDatabaseManager.IsCorrupt && projectDatabaseManager.ProjectDatabase != null)
				{
					projectDatabaseManager.ProjectDatabase.VerboseLogging = e.newValue;
					projectDatabaseManager.SaveProjectDatabase();
				}

				Logger.EnableVerbose(e.newValue);
			};
			verboseLogging.RegisterValueChangedCallback(verboseLoggingCallback);
		}

		void ReadSettingsFromProjectDatabase()
		{
			verboseLogging.value = projectDatabaseManager.ProjectDatabase.VerboseLogging;
			Logger.EnableVerbose(verboseLogging.value);
		}

		Box corruptionDetectedOverlay;
		Button tryAgainButton;
		Action tryAgainButtonClickEvent;
		Button recreateButton;
		Action recreateButtonClickEvent;

		void InitializeProjectDatabase()
		{
			corruptionDetectedOverlay = UIRootVisualElement.Q<Box>("corruption-detected-overlay");

			tryAgainButton = corruptionDetectedOverlay.Q<Button>("try-again");
			recreateButton = corruptionDetectedOverlay.Q<Button>("recreate");
			QueuedActions.Add(() => SetVisible(corruptionDetectedOverlay, false));

			if (projectDatabaseManager == null)
			{
				projectDatabaseManager = new ProjectDatabaseManager();

				projectDatabaseManager.OnCorruptionDetected += OnProjectDatabaseCorruptionDetected;
				projectDatabaseManager.OnPostAssetPostProcess += OnPostAssetPostProcess;

				if (!projectDatabaseManager.IsCorrupt && projectDatabaseManager.ProjectDatabase != null)
				{
					OnProjectDatabaseLoad();
				}

				projectDatabaseManager.OnDatabaseLoad += OnProjectDatabaseLoad;
			}

			if (tryAgainButtonClickEvent != null)
				tryAgainButton.clicked -= tryAgainButtonClickEvent;
			tryAgainButtonClickEvent = () =>
			{
				QueuedActions.Add(() => SetVisible(corruptionDetectedOverlay, false));

				projectDatabaseManager.LoadProjectDatabaseIfNeeded(true);
			};
			tryAgainButton.clicked += tryAgainButtonClickEvent;

			if (recreateButtonClickEvent != null)
				recreateButton.clicked -= recreateButtonClickEvent;
			recreateButtonClickEvent = () =>
			{
				QueuedActions.Add(() => SetVisible(corruptionDetectedOverlay, false));

				projectDatabaseManager.LoadProjectDatabaseIfNeeded(false);
			};
			recreateButton.clicked += recreateButtonClickEvent;
		}

		void OnProjectDatabaseLoad()
		{
			ReadSettingsFromProjectDatabase();
		}

		void OnProjectDatabaseCorruptionDetected()
		{
			SetVisible(corruptionDetectedOverlay, true);
		}

		Dictionary<string, (ToolbarButton, Box)> tabList = new();
		string currentTab = "";

		void InitializeTabview()
		{
			tabList.Add("repo-auth-tab", (UIRootVisualElement.Q<ToolbarButton>("repo-auth-tab-button"), UIRootVisualElement.Q<Box>("repo-auth-tab")));
			tabList.Add("repo-manager-tab", (UIRootVisualElement.Q<ToolbarButton>("repo-manager-tab-button"), UIRootVisualElement.Q<Box>("repo-manager-tab")));
			tabList.Add("mod-builder-tab", (UIRootVisualElement.Q<ToolbarButton>("mod-builder-tab-button"), UIRootVisualElement.Q<Box>("mod-builder-tab")));
			tabList.Add("settings-tab", (UIRootVisualElement.Q<ToolbarButton>("settings-tab-button"), UIRootVisualElement.Q<Box>("settings-tab")));

			foreach (string tab in tabList.Keys)
			{
				tabList[tab].Item1.clicked += () =>
				{
					SelectTab(tab);
				};
			}

			SelectTab("repo-auth-tab");
		}

		void SelectTab(string tab)
		{
			if (!tabList.ContainsKey(tab))
			{
				return;
			}

			currentTab = tab;

			foreach (string tabIter in tabList.Keys)
			{
				QueuedActions.Add(() =>
				{
					SetVisible(tabList[tabIter].Item2, tab == tabIter);

					if (tab == tabIter)
					{
						tabList[tabIter].Item1.AddPsuedoState(1);
					}
					else
					{
						tabList[tabIter].Item1.RemovePsuedoState(1);
					}
				});
			}
		}

		public ModPackagerRepositoryManager repositoryManager = null;

		void InitializeAuthentication()
		{
			repositoryManager = new ModPackagerRepositoryManager();

			repositoryManager.OnRepositoryLoaded += OnRepositoryLoaded;
			repositoryManager.OnRepositoryLoadFailed += OnRepositoryLoadFailed;
			repositoryManager.OnRepositoryIndexProgress += OnRepositoryIndexProgress;
			repositoryManager.OnRepositoryIndexed += OnRepositoryIndexed;

			repositoryManager.OnSignInSuccess += OnSignInSuccess;
			repositoryManager.OnSignInFailed += OnSignInFailed;
			repositoryManager.OnSignOut += OnSignOut;
			repositoryManager.OnMFAChallenge += OnMFAChallenge;
			repositoryManager.OnNewPasswordChallenge += OnNewPasswordChallenge;
		}

		Box loadRepositoryBox;
		Label activeRepository;
		Label repositoryLoadFailMessage;
		TextField repositoryUrlField;
		Button loadRepositoryButton;
		ProgressBar loadRepositoryProgressBar;

		Label manageRepositoryTitle;
		Button manageRepositoryReloadButton;
		ProgressBar repositoryIndexProgressBar;
		ToolbarSearchField modSearchField;
		List<EventCallback<ChangeEvent<string>>> modSearchChangedCallbacks = new();
		ScrollView modsScrollview;
		List<ModPackagerRepositoryManager.OnDeleteProgressEvent> deleteProgressEventCallbacks = new();

		Box signInBox;
		Label signInHeader;
		Label signInFailMessage;
		TextField usernameField;
		TextField passwordField;
		Button signInButton;
		ProgressBar signInProgressBar;
		Box mfaChallengeBox;
		Box newPasswordBox;
		Box signOutBox;
		Label signOutHeader;
		Button signOutButton;

		TextField newPasswordField;
		Button newPasswordSubmitButton;
		TextField mfaCodeField;
		Button mfaCodeSubmitButton;

		Box buildBox;
		Box buildPublishBox;

		void InitializeAuthenticationSection()
		{
			loadRepositoryBox = UIRootVisualElement.Q<Box>("load-repository-box");
			activeRepository = loadRepositoryBox.Q<Label>("active-repository");
			repositoryLoadFailMessage = loadRepositoryBox.Q<Label>("repository-load-fail-message");
			repositoryUrlField = loadRepositoryBox.Q<TextField>("repository-url-field");
			loadRepositoryButton = loadRepositoryBox.Q<Button>("load-repository-button");
			loadRepositoryProgressBar = loadRepositoryBox.Q<ProgressBar>("load-repository-progress-bar");

			manageRepositoryTitle = UIRootVisualElement.Q<Label>("manage-repo-title");
			manageRepositoryReloadButton = UIRootVisualElement.Q<Button>("manage-repo-reload-button");
			repositoryIndexProgressBar = UIRootVisualElement.Q<ProgressBar>("repository-index-progress-bar");

			manageRepositoryReloadButton.clicked += () =>
			{
				repositoryManager.IndexMods();
			};

			modSearchField = UIRootVisualElement.Q<ToolbarSearchField>("mods-search");
			modsScrollview = UIRootVisualElement.Q<ScrollView>("mods-scrollview");

			signInBox = UIRootVisualElement.Q<Box>("sign-in-box");
			signInHeader = signInBox.Q<Label>("sign-in-header");
			signInFailMessage = signInBox.Q<Label>("sign-in-fail-message");
			usernameField = signInBox.Q<TextField>("username-field");
			passwordField = signInBox.Q<TextField>("password-field");
			signInButton = signInBox.Q<Button>("sign-in-button");
			signInProgressBar = signInBox.Q<ProgressBar>("sign-in-progress-bar");
			mfaChallengeBox = UIRootVisualElement.Q<Box>("mfa-challenge-box");
			newPasswordBox = UIRootVisualElement.Q<Box>("new-password-box");
			signOutBox = UIRootVisualElement.Q<Box>("sign-out-box");
			signOutHeader = signOutBox.Q<Label>("sign-out-header");
			signOutButton = signOutBox.Q<Button>("sign-out-button");

			newPasswordField = newPasswordBox.Q<TextField>("new-password-field");
			newPasswordSubmitButton = newPasswordBox.Q<Button>("new-password-button");
			newPasswordSubmitButton.clicked += () =>
			{
				newPasswordWaitHandle.Set();
			};

			mfaCodeField = mfaChallengeBox.Q<TextField>("mfa-code-field");
			mfaCodeSubmitButton = mfaChallengeBox.Q<Button>("submit-mfa-button");
			mfaCodeSubmitButton.clicked += () =>
			{
				mfaChallengeWaitHandle.Set();
			};

			buildBox = UIRootVisualElement.Q<Box>("build-box");
			buildPublishBox = UIRootVisualElement.Q<Box>("build-publish-box");

			QueuedActions.Add(() =>
			{
				repositoryUrlField.SetEnabled(true);
				signInButton.SetEnabled(false);
				manageRepositoryReloadButton.SetEnabled(false);

				SetVisible(repositoryLoadFailMessage, false);
				SetVisible(loadRepositoryButton, true);
				SetVisible(loadRepositoryProgressBar, false);
				SetVisible(repositoryIndexProgressBar, false);
				SetVisible(manageRepositoryReloadButton, true);

				UIRootVisualElement.MarkDirtyRepaint();
			});

			loadRepositoryButton.clicked += () =>
			{
				QueuedActions.Add(() =>
				{
					activeRepository.text = $"Loading {repositoryUrlField.value}...";
					manageRepositoryTitle.text = $"Loading {repositoryUrlField.value}...";

					repositoryUrlField.SetEnabled(false);
					signInButton.SetEnabled(false);
					manageRepositoryReloadButton.SetEnabled(false);

					SetVisible(repositoryLoadFailMessage, false);
					SetVisible(loadRepositoryButton, false);
					SetVisible(loadRepositoryProgressBar, true);

					UIRootVisualElement.MarkDirtyRepaint();
				});

				repositoryManager.SwitchRepository(repositoryUrlField.value);
			};

			QueuedActions.Add(() =>
			{
				usernameField.SetEnabled(true);
				passwordField.SetEnabled(true);

				SetVisible(signInBox, true);
				SetVisible(signInFailMessage, false);
				SetVisible(signInButton, true);
				SetVisible(signInProgressBar, false);
				SetVisible(mfaChallengeBox, false);
				SetVisible(newPasswordBox, false);
				SetVisible(signOutBox, false);
				SetVisible(buildBox, true);
				SetVisible(buildPublishBox, false);

				UIRootVisualElement.MarkDirtyRepaint();
			});

			signInButton.clicked += () =>
			{
				QueuedActions.Add(() =>
				{
					usernameField.SetEnabled(false);
					passwordField.SetEnabled(false);

					SetVisible(signInBox, true);
					SetVisible(signInFailMessage, false);
					SetVisible(signInButton, false);
					SetVisible(signInProgressBar, true);
					SetVisible(mfaChallengeBox, false);
					SetVisible(newPasswordBox, false);
					SetVisible(signOutBox, false);
					SetVisible(buildBox, true);
					SetVisible(buildPublishBox, false);

					UIRootVisualElement.MarkDirtyRepaint();
				});

				repositoryManager.SignIn(usernameField.value, passwordField.value);
			};

			signOutButton.clicked += () =>
			{
				OnSignOut();

				repositoryManager.SignOut();
			};
		}

		void OnRepositoryLoaded()
		{
			if (repositoryManager.repositoryMetadata == null)
			{
				QueuedActions.Add(() =>
				{
					activeRepository.text = "No Mod Repository Loaded";
					signInHeader.text = "Sign in to the Mod Repository:";
					signOutHeader.text = "Signed in to the Mod Repository";

					manageRepositoryTitle.text = $"No Mod Repository Loaded";

					signInButton.SetEnabled(false);

					modsScrollview.Clear();

					while (modSearchChangedCallbacks.Count > 0)
					{
						modSearchField.UnregisterCallback(modSearchChangedCallbacks[0]);
						modSearchChangedCallbacks.RemoveAt(0);
					}
				});
			}
			else
			{
				QueuedActions.Add(() =>
				{
					activeRepository.text = $"Loaded Repository: {repositoryManager.repositoryMetadata.RepoName}";
					signInHeader.text = $"Sign in to {repositoryManager.repositoryMetadata.RepoName}:";
					signOutHeader.text = $"Signed in to {repositoryManager.repositoryMetadata.RepoName}";

					manageRepositoryTitle.text = $"Loaded Repository: {repositoryManager.repositoryMetadata.RepoName}{(repositoryManager.IsAuthenticated ? "" : ", Not Signed In")}";

					repositoryUrlField.value = repositoryManager.TargetRepository;

					signInButton.SetEnabled(true);
				});

				if (repositoryManager.IsAuthenticated)
					repositoryManager.IndexMods();
			}
			QueuedActions.Add(() =>
			{
				repositoryUrlField.SetEnabled(true);

				SetVisible(repositoryLoadFailMessage, false);
				SetVisible(loadRepositoryButton, true);
				SetVisible(loadRepositoryProgressBar, false);

				UIRootVisualElement.MarkDirtyRepaint();
			});

			OnSignOut();
		}

		void OnRepositoryLoadFailed()
		{
			if (repositoryManager.repositoryMetadata == null)
			{
				QueuedActions.Add(() =>
				{
					activeRepository.text = "No Mod Repository Loaded";
					signInHeader.text = "Sign in to the Mod Repository:";
					signOutHeader.text = "Signed in to the Mod Repository";

					manageRepositoryTitle.text = $"No Mod Repository Loaded";

					signInButton.SetEnabled(false);

					modsScrollview.Clear();
				});

				while (modSearchChangedCallbacks.Count > 0)
				{
					modSearchField.UnregisterCallback(modSearchChangedCallbacks[0]);
					modSearchChangedCallbacks.RemoveAt(0);
				}
			}
			else
			{
				QueuedActions.Add(() =>
				{
					activeRepository.text = $"Loaded Repository: {repositoryManager.repositoryMetadata.RepoName}";
					signInHeader.text = $"Sign in to {repositoryManager.repositoryMetadata.RepoName}:";
					signOutHeader.text = $"Signed in to {repositoryManager.repositoryMetadata.RepoName}";

					manageRepositoryTitle.text = $"Loaded Repository: {repositoryManager.repositoryMetadata.RepoName}{(repositoryManager.IsAuthenticated ? "" : ", Not Signed In")}";

					repositoryUrlField.value = repositoryManager.TargetRepository;

					signInButton.SetEnabled(true);
				});

				if (repositoryManager.IsAuthenticated)
					repositoryManager.IndexMods();
			}
			QueuedActions.Add(() =>
			{
				repositoryUrlField.SetEnabled(true);

				SetVisible(repositoryLoadFailMessage, true);
				SetVisible(loadRepositoryButton, true);
				SetVisible(loadRepositoryProgressBar, false);

				UIRootVisualElement.MarkDirtyRepaint();
			});

			OnSignOut();
		}

		CancellationTokenSource repositoryIndexTimeoutCancellationTokenSource = new CancellationTokenSource();

		void OnRepositoryIndexProgress(int progress)
		{
			repositoryIndexTimeoutCancellationTokenSource.Cancel();
			repositoryIndexTimeoutCancellationTokenSource.Dispose();

			repositoryIndexTimeoutCancellationTokenSource = new CancellationTokenSource();

			QueuedActions.Add(() =>
			{
				if (repositoryIndexProgressBar == null)
					return;

				VisualElement title = repositoryIndexProgressBar.Q<VisualElement>(className: "unity-progress-bar__title");
				VisualElement background = repositoryIndexProgressBar.Q<VisualElement>(className: "unity-progress-bar__progress");

				title.style.paddingLeft = 8;
				title.style.paddingRight = 8;

				if (progress == -2)
				{
					repositoryIndexProgressBar.title = $"Index Failed";
					repositoryIndexProgressBar.value = 100;
					background.style.backgroundColor = new StyleColor(new Color(0.529411764706f, 0.172549019608f, 0.172549019608f, 1));

					_ = Task.Delay(TimeSpan.FromSeconds(3), repositoryIndexTimeoutCancellationTokenSource.Token)
						.ContinueWith(
							(task) =>
								QueuedActions.Add(() =>
								{
									SetVisible(repositoryIndexProgressBar, false);
									SetVisible(manageRepositoryReloadButton, true);
								})
						);
					// "#872C2C"
				}
				else if (progress == -1)
				{
					repositoryIndexProgressBar.title = $"Index Pending...";
					repositoryIndexProgressBar.value = 0;
					background.style.backgroundColor = new StyleColor(new Color(0.172549019608f, 0.364705882353f, 0.529411764706f, 1));

					SetVisible(repositoryIndexProgressBar, true);
					SetVisible(manageRepositoryReloadButton, false);
					// "#2C5D87"
				}
				else if (progress >= 100)
				{
					repositoryIndexProgressBar.title = $"Done Indexing";
					repositoryIndexProgressBar.value = 100;
					background.style.backgroundColor = new StyleColor(new Color(0.172549019608f, 0.529411764706f, 0.172549019608f, 1));

					_ = Task.Delay(TimeSpan.FromSeconds(3), repositoryIndexTimeoutCancellationTokenSource.Token)
						.ContinueWith(
							(task) =>
								QueuedActions.Add(() =>
								{
									SetVisible(repositoryIndexProgressBar, false);
									SetVisible(manageRepositoryReloadButton, true);
								})
						);
					// "#2C872C"
				}
				else
				{
					repositoryIndexProgressBar.title = $"Indexing...";
					repositoryIndexProgressBar.value = progress;
					background.style.backgroundColor = new StyleColor(new Color(0.172549019608f, 0.364705882353f, 0.529411764706f, 1));

					SetVisible(repositoryIndexProgressBar, true);
					SetVisible(manageRepositoryReloadButton, false);
					// "#2C5D87"
				}
			});
		}

		void OnRepositoryIndexed()
		{
			QueuedActions.Add(() =>
			{
				modsScrollview.Clear();
			});

			while (modSearchChangedCallbacks.Count > 0)
			{
				modSearchField.UnregisterCallback(modSearchChangedCallbacks[0]);
				modSearchChangedCallbacks.RemoveAt(0);
			}

			QueuedActions.Add(() =>
			{
				modSearchField.value = "";
			});

			foreach ((ModMetadata, Dictionary<BuildTarget, bool>) modData in repositoryManager.repositoryMods.Values)
			{
				string searchIndex = GenerateModSearchIndex(modData.Item1);
				Guid deleteUuid = modData.Item1.Uuid;

				QueuedActions.Add(() =>
				{
					ModListElement newListElementRendered = new ModListElement(repositoryManager.repositoryMetadata.BaseURL, modData.Item1, modData.Item2);

					newListElementRendered.onDelete += () =>
					{
						repositoryManager.DeleteMod(deleteUuid);
					};

					ModPackagerRepositoryManager.OnDeleteProgressEvent deleteProgressEvent = (mod, progress) =>
					{
						if (mod == modData.Item1.Uuid)
						{
							newListElementRendered.UpdateDeleteionProgress(progress);
						}
					};

					deleteProgressEventCallbacks.Add(deleteProgressEvent);
					repositoryManager.OnDeleteProgress += deleteProgressEvent;

					EventCallback<ChangeEvent<string>> searchCallback = (ChangeEvent<string> changeEvent) =>
					{
						string newValue = changeEvent.newValue;
						QueuedActions.Add(() =>
						{
							SetVisible(newListElementRendered, ModFilterCheck(newValue, searchIndex));
						});
					};

					modSearchChangedCallbacks.Add(searchCallback);
					modSearchField.RegisterCallback(searchCallback);

					modsScrollview.Add(newListElementRendered);
				});
			}
		}

		bool ModFilterCheck(string query, string searchIndex)
		{
			return query == "" || Fuzz.PartialTokenSetRatio(searchIndex.ToLower(), query.ToLower()) > 90;
		}

		string GenerateModSearchIndex(ModMetadata metadata)
		{
			return $"{metadata.Name} {metadata.Uuid} {metadata.Owner} {string.Join(" ", metadata.Creators)}";
		}

		void OnSignInSuccess()
		{
			QueuedActions.Add(() =>
			{
				usernameField.SetEnabled(false);
				passwordField.SetEnabled(false);
				manageRepositoryReloadButton.SetEnabled(true);
			});
			if (repositoryManager.repositoryMetadata == null)
			{
				QueuedActions.Add(() =>
				{
					manageRepositoryTitle.text = $"No Mod Repository Loaded";
				});
			}
			else
			{
				QueuedActions.Add(() =>
				{
					manageRepositoryTitle.text = $"Loaded Repository: {repositoryManager.repositoryMetadata.RepoName}";
				});
			}

			QueuedActions.Add(() =>
			{
				SetVisible(signInBox, false);
				SetVisible(signInFailMessage, false);
				SetVisible(signInButton, false);
				SetVisible(signInProgressBar, false);
				SetVisible(mfaChallengeBox, false);
				SetVisible(newPasswordBox, false);
				SetVisible(signOutBox, true);
				SetVisible(buildBox, false);
				SetVisible(buildPublishBox, true);

				UIRootVisualElement.MarkDirtyRepaint();
			});
		}

		void OnSignInFailed()
		{
			QueuedActions.Add(() =>
			{
				usernameField.SetEnabled(true);
				passwordField.SetEnabled(true);

				SetVisible(signInBox, true);
				SetVisible(signInFailMessage, true);
				SetVisible(signInButton, true);
				SetVisible(signInProgressBar, false);
				SetVisible(mfaChallengeBox, false);
				SetVisible(newPasswordBox, false);
				SetVisible(signOutBox, false);
				SetVisible(buildBox, true);
				SetVisible(buildPublishBox, false);

				UIRootVisualElement.MarkDirtyRepaint();
			});
		}

		void OnSignOut()
		{
			QueuedActions.Add(() =>
			{
				usernameField.SetEnabled(true);
				passwordField.SetEnabled(true);
				manageRepositoryReloadButton.SetEnabled(false);

				modsScrollview.Clear();
			});

			if (repositoryManager.repositoryMetadata == null)
			{
				QueuedActions.Add(() =>
				{
					manageRepositoryTitle.text = $"No Mod Repository Loaded";
				});
			}
			else
			{
				QueuedActions.Add(() =>
				{
					manageRepositoryTitle.text = $"Loaded Repository: {repositoryManager.repositoryMetadata.RepoName}, Not Signed In";
				});
			}

			while (modSearchChangedCallbacks.Count > 0)
			{
				modSearchField.UnregisterCallback(modSearchChangedCallbacks[0]);
				modSearchChangedCallbacks.RemoveAt(0);
			}

			QueuedActions.Add(() =>
			{
				SetVisible(signInBox, true);
				SetVisible(signInFailMessage, false);
				SetVisible(signInButton, true);
				SetVisible(signInProgressBar, false);
				SetVisible(mfaChallengeBox, false);
				SetVisible(newPasswordBox, false);
				SetVisible(signOutBox, false);
				SetVisible(buildBox, true);
				SetVisible(buildPublishBox, false);

				UIRootVisualElement.MarkDirtyRepaint();
			});
		}

		ManualResetEvent mfaChallengeWaitHandle = new ManualResetEvent(false);

		string OnMFAChallenge()
		{
			QueuedActions.Add(() =>
			{
				usernameField.SetEnabled(false);
				passwordField.SetEnabled(false);

				SetVisible(signInBox, false);
				SetVisible(signInFailMessage, false);
				SetVisible(signInButton, false);
				SetVisible(signInProgressBar, true);
				SetVisible(mfaChallengeBox, true);
				SetVisible(newPasswordBox, false);
				SetVisible(signOutBox, false);
				SetVisible(buildBox, true);
				SetVisible(buildPublishBox, false);

				UIRootVisualElement.MarkDirtyRepaint();
			});

			mfaChallengeWaitHandle.WaitOne();
			mfaChallengeWaitHandle.Reset();

			string mfaCode = mfaCodeField.value;

			QueuedActions.Add(() =>
			{
				usernameField.SetEnabled(false);
				passwordField.SetEnabled(false);

				SetVisible(signInBox, false);
				SetVisible(signInFailMessage, false);
				SetVisible(signInButton, false);
				SetVisible(signInProgressBar, true);
				SetVisible(mfaChallengeBox, false);
				SetVisible(newPasswordBox, false);
				SetVisible(signOutBox, false);
				SetVisible(buildBox, true);
				SetVisible(buildPublishBox, false);
			});

			return mfaCode;
		}

		ManualResetEvent newPasswordWaitHandle = new ManualResetEvent(false);

		string OnNewPasswordChallenge()
		{
			QueuedActions.Add(() =>
			{
				usernameField.SetEnabled(false);
				passwordField.SetEnabled(false);

				SetVisible(signInBox, false);
				SetVisible(signInFailMessage, false);
				SetVisible(signInButton, false);
				SetVisible(signInProgressBar, true);
				SetVisible(mfaChallengeBox, false);
				SetVisible(newPasswordBox, true);
				SetVisible(signOutBox, false);
				SetVisible(buildBox, true);
				SetVisible(buildPublishBox, false);

				UIRootVisualElement.MarkDirtyRepaint();
			});

			newPasswordWaitHandle.WaitOne();
			newPasswordWaitHandle.Reset();

			string newPassword = newPasswordField.value;

			QueuedActions.Add(() =>
			{
				usernameField.SetEnabled(false);
				passwordField.SetEnabled(false);

				SetVisible(signInBox, false);
				SetVisible(signInFailMessage, false);
				SetVisible(signInButton, false);
				SetVisible(signInProgressBar, true);
				SetVisible(mfaChallengeBox, false);
				SetVisible(newPasswordBox, false);
				SetVisible(signOutBox, false);
				SetVisible(buildBox, true);
				SetVisible(buildPublishBox, false);
			});

			return newPassword;
		}

		Button targetAssetbundleReloadButton;
		ProgressBar assetbundleReloadProgressBar;
		DropdownField targetAssetbundleDropdown;
		EventCallback<ChangeEvent<string>> targetAssetbundleDropdownChangeEvent;

		void InitializeModSetupSection()
		{
			targetAssetbundleReloadButton = UIRootVisualElement.Q<Button>("target-assetbundle-reload-button");
			assetbundleReloadProgressBar = UIRootVisualElement.Q<ProgressBar>("assetbundle-reload-progress-bar");

			QueuedActions.Add(() =>
			{
				SetVisible(targetAssetbundleReloadButton, true);
				SetVisible(assetbundleReloadProgressBar, false);
			});

			targetAssetbundleReloadButton.clicked += () =>
			{
				LoadAssetBundle(targetAssetBundle);
			};

			projectDatabaseManager.IndexProjectDatabase(); // Reindex all assets so we can get the asset bundle list
			ReloadAssetBundleSelector();

			LoadAssetBundle(null);
		}

		void ReloadAssetBundleSelector()
		{
			targetAssetbundleDropdown = UIRootVisualElement.Q<DropdownField>("target-assetbundle");
			if (targetAssetbundleDropdownChangeEvent != null)
				targetAssetbundleDropdown.UnregisterCallback(targetAssetbundleDropdownChangeEvent);

			string oldValue = targetAssetbundleDropdown.value;

			targetAssetbundleDropdown.choices.Clear();

			foreach (string assetbundle in AssetDatabase.GetAllAssetBundleNames())
			{
				targetAssetbundleDropdown.choices.Add(assetbundle);
			}

			if (oldValue != null && targetAssetbundleDropdown.choices.Contains(oldValue))
			{
				targetAssetbundleDropdown.value = oldValue;
				targetAssetBundle = oldValue;
			}
			else
			{
				targetAssetbundleDropdown.value = null;
				targetAssetBundle = null;
			}

			targetAssetbundleDropdownChangeEvent = (evt) =>
			{
				targetAssetbundleDropdown.value = evt.newValue;

				LoadAssetBundle(evt.newValue);
			};
			targetAssetbundleDropdown.RegisterCallback(targetAssetbundleDropdownChangeEvent);
		}

		Box modConfigBox;
		CustomPopupButton buildSelectorButton;
		Button buildButton;
		Action buildButtonAction;
		CustomPopupButton buildPublishSelectorButton;
		Button buildPublishButton;
		Action buildPublishButtonAction;
		bool buildButtonValidInContext = false;

		ToolbarSearchField assetSearchField;
		List<EventCallback<ChangeEvent<string>>> assetSearchChangedCallbacks = new();
		MaskField assetFilterField;
		List<string> assetTypeList = new();
		List<EventCallback<ChangeEvent<int>>> assetFilterChangedCallbacks = new();
		List<EventCallback<ChangeEvent<string>>> assetAttributionChangedCallback = new(); // used to update all obj attribution in bundle

		ScrollView assetsScrollview;

		TextField modNameField;
		EventCallback<ChangeEvent<string>> modNameFieldChangeEvent;
		TextField modOwnerField;
		EventCallback<ChangeEvent<string>> modOwnerFieldChangeEvent;
		TextField modAttributionField;
		EventCallback<ChangeEvent<string>> modAttributionFieldChangeEvent;
		DropdownField modPresetAttributions;
		EventCallback<ChangeEvent<string>> modPresetAttributionChangeEvent;
		ListView modVersionList;
		List<BuildTarget> modVersionListTargetOrder = new();
		Dictionary<IntegerField, EventCallback<ChangeEvent<int>>> modVersionListChangeEvents = new();
		ListView modCreatorList;
		Dictionary<TextField, EventCallback<ChangeEvent<string>>> modCreatorListChangeEvents = new();
		Action<IEnumerable<int>> previousListAddedEvent;
		Action<IEnumerable<int>> previousListRemovedEvent;

		CancellationTokenSource assetbundleIndexTimeoutCancellationTokenSource = new CancellationTokenSource();

		void OnAssetBundleLoadProgress(int progress)
		{
			assetbundleIndexTimeoutCancellationTokenSource.Cancel();
			assetbundleIndexTimeoutCancellationTokenSource.Dispose();

			assetbundleIndexTimeoutCancellationTokenSource = new CancellationTokenSource();

			QueuedActions.Add(() =>
			{
				if (assetbundleReloadProgressBar == null)
					return;

				VisualElement title = assetbundleReloadProgressBar.Q<VisualElement>(className: "unity-progress-bar__title");
				VisualElement background = assetbundleReloadProgressBar.Q<VisualElement>(className: "unity-progress-bar__progress");

				title.style.paddingLeft = 8;
				title.style.paddingRight = 8;

				if (progress == -2)
				{
					assetbundleReloadProgressBar.title = $"Reload Failed";
					assetbundleReloadProgressBar.value = 100;
					background.style.backgroundColor = new StyleColor(new Color(0.529411764706f, 0.172549019608f, 0.172549019608f, 1));

					_ = Task.Delay(TimeSpan.FromSeconds(3), assetbundleIndexTimeoutCancellationTokenSource.Token)
						.ContinueWith(
							(task) =>
								QueuedActions.Add(() =>
								{
									SetVisible(assetbundleReloadProgressBar, false);
									SetVisible(targetAssetbundleReloadButton, true);
								})
						);
					// "#872C2C"
				}
				else if (progress == -1)
				{
					assetbundleReloadProgressBar.title = $"Reload Pending...";
					assetbundleReloadProgressBar.value = 0;
					background.style.backgroundColor = new StyleColor(new Color(0.172549019608f, 0.364705882353f, 0.529411764706f, 1));

					SetVisible(assetbundleReloadProgressBar, true);
					SetVisible(targetAssetbundleReloadButton, false);
					// "#2C5D87"
				}
				else if (progress >= 100)
				{
					assetbundleReloadProgressBar.title = $"Done Reloading";
					assetbundleReloadProgressBar.value = 100;
					background.style.backgroundColor = new StyleColor(new Color(0.172549019608f, 0.529411764706f, 0.172549019608f, 1));

					_ = Task.Delay(TimeSpan.FromSeconds(3), assetbundleIndexTimeoutCancellationTokenSource.Token)
						.ContinueWith(
							(task) =>
								QueuedActions.Add(() =>
								{
									SetVisible(assetbundleReloadProgressBar, false);
									SetVisible(targetAssetbundleReloadButton, true);
								})
						);
					// "#2C872C"
				}
				else
				{
					assetbundleReloadProgressBar.title = $"Reloading...";
					assetbundleReloadProgressBar.value = progress;
					background.style.backgroundColor = new StyleColor(new Color(0.172549019608f, 0.364705882353f, 0.529411764706f, 1));

					SetVisible(assetbundleReloadProgressBar, true);
					SetVisible(targetAssetbundleReloadButton, false);
					// "#2C5D87"
				}
			});
		}

		/// <summary>
		/// Updates the project database, reserializing and reimporting all files, before redrawing the Asset Bundle UI.
		/// </summary>
		void LoadAssetBundle(string assetbundle)
		{
			QueuedActions.Add(() =>
			{
				OnAssetBundleLoadProgress(-1);
			});

			// If we're just drawing the menu with no asset bundle selected,
			// we likely don't need to reimport everything until the user picks an option.
			// This will get called again when the user picks said option,
			// which is when it's more important to have an up-to-date database.
			if (assetbundle != null)
			{
				projectDatabaseManager.ReimportAssets();
				projectDatabaseManager.SaveAssetBundle(assetbundle);
			}

			targetAssetBundle = assetbundle;

			ReloadAssetBundleUI(targetAssetBundle);
		}

		void OnPostAssetPostProcess()
		{
			ReloadAssetBundleSelector();

			QueuedActions.Add(() =>
			{
				OnAssetBundleLoadProgress(-1);
			});

			ReloadAssetBundleUI(targetAssetBundle);
		}

		void ReloadAssetBundleUI(string assetbundle)
		{
			///
			/// STEP 1:
			/// Setup UI references and clear any previous data
			/// ----------------------------------------------------------------------------------


			QueuedActions.Add(() =>
			{
				// init progress load bar to 0 
				OnAssetBundleLoadProgress(0);
			});

			// setup references to all the UI elements we need to update
			modConfigBox = UIRootVisualElement.Q<Box>("mod-config-box");

			modNameField = modConfigBox.Q<TextField>("mod-name-field");
			modOwnerField = modConfigBox.Q<TextField>("mod-owner-field");
			modAttributionField = modConfigBox.Q<TextField>("mod-attribution-field");
			modPresetAttributions = modConfigBox.Q<DropdownField>("mod-preset-attributions");
			modVersionList = modConfigBox.Q<ListView>("mod-versions-list");
			modCreatorList = modConfigBox.Q<ListView>("mod-creators-list");

			assetSearchField = UIRootVisualElement.Q<ToolbarSearchField>("asset-search");
			assetFilterField = UIRootVisualElement.Q<MaskField>("asset-filter");
			assetsScrollview = UIRootVisualElement.Q<ScrollView>("assets-scrollview");

			buildSelectorButton = UIRootVisualElement.Q<CustomPopupButton>("build-selector");
			buildButton = UIRootVisualElement.Q<Button>("build");
			buildPublishSelectorButton = UIRootVisualElement.Q<CustomPopupButton>("build-publish-selector");
			buildPublishButton = UIRootVisualElement.Q<Button>("build-publish");

			QueuedActions.Add(() =>
			{
				// remove all previous asset list elements from the scrollview
				assetsScrollview.Clear();
			});

			// clear callbacks
			while (assetSearchChangedCallbacks.Count > 0)
			{
				assetSearchField.UnregisterCallback(assetSearchChangedCallbacks[0]);
				assetSearchChangedCallbacks.RemoveAt(0);
			}

			// clear any asset search fields, filters, and filter callbacks
			QueuedActions.Add(() =>
			{
				assetSearchField.value = "";
			});
			QueuedActions.Add(() =>
			{
				assetTypeList.Clear();
			});
			while (assetFilterChangedCallbacks.Count > 0)
			{
				assetFilterField.UnregisterCallback(assetFilterChangedCallbacks[0]);
				assetFilterChangedCallbacks.RemoveAt(0);
			}


			///
			/// STEP 2:
			/// Initialize Asset UI if assetBundle exists
			/// ----------------------------------------------------------------------------------


			if (assetbundle == null)
			{
				QueuedActions.Add(() =>
				{
					assetFilterField.value = 0;
					assetFilterField.choices = new List<string> { };

					assetSearchField.SetEnabled(false);
					assetFilterField.SetEnabled(false);

					buildSelectorButton.SetEnabled(false);
					buildButton.SetEnabled(false);
					buildPublishSelectorButton.SetEnabled(false);
					buildPublishButton.SetEnabled(false);

					buildButtonValidInContext = false;

					SetVisible(modConfigBox, false);
				});

				QueuedActions.Add(() =>
				{
					OnAssetBundleLoadProgress(100);
				});

				return;
			}
			else
			{
				QueuedActions.Add(() =>
				{
					assetSearchField.SetEnabled(true);
					assetFilterField.SetEnabled(true);

					buildSelectorButton.SetEnabled(true);
					buildButton.SetEnabled(true && buildTargetSelector.IsAtLeastOneSelected());
					buildPublishSelectorButton.SetEnabled(true);
					buildPublishButton.SetEnabled(true && buildTargetSelector.IsAtLeastOneSelected());

					buildButtonValidInContext = true;

					SetVisible(modConfigBox, true);
				});
			}
			// setup "action counters" to track loading progress
			float totalActions = projectDatabaseManager.TryGetModMetadata(assetbundle).AssetMap.Keys.Count + 2;
			float actionsDone = 0;


			///
			/// STEP 3:
			/// Populate Mod Metadata UI fields with data from the project database
			/// ----------------------------------------------------------------------------------

			ModMetadata modMetadataRef = projectDatabaseManager.TryGetModMetadata(assetbundle);
			ModEditorData modExtraDataRef = projectDatabaseManager.TryGetModEditorData(assetbundle);

			// NAME
			modNameField.value = modMetadataRef.Name;
			if (modNameFieldChangeEvent != null)
				modNameField.UnregisterCallback(modNameFieldChangeEvent);
			modNameFieldChangeEvent = (evt) =>
			{
				modMetadataRef.Name = evt.newValue;

				projectDatabaseManager.SaveAssetBundle(assetbundle);
			};
			modNameField.RegisterCallback(modNameFieldChangeEvent);

			// OWNER
			modOwnerField.value = modMetadataRef.Owner;
			if (modOwnerFieldChangeEvent != null)
				modOwnerField.UnregisterCallback(modOwnerFieldChangeEvent);
			modOwnerFieldChangeEvent = (evt) =>
			{
				modMetadataRef.Owner = evt.newValue;

				projectDatabaseManager.SaveAssetBundle(assetbundle);
			};
			modOwnerField.RegisterCallback(modOwnerFieldChangeEvent);

			// TYPE OF ATTRIBUTION (DROPDOWN)
			modPresetAttributions.choices = AssetListPrefabElement.PresetAttributions;
			modPresetAttributions.value = AssetListPrefabElement.PresetAttributions[0];
			if (modPresetAttributionChangeEvent != null)
				modPresetAttributions.UnregisterCallback(modPresetAttributionChangeEvent);
			modPresetAttributionChangeEvent = (evt) =>
			{
				if (evt.newValue.Equals("Custom"))
				{
					return;
				}

				modAttributionField.value = evt.newValue; // on selecting drop down option, update text field
			};
			modPresetAttributions.RegisterCallback(modPresetAttributionChangeEvent);

			// ATTRIBUTION
			modAttributionField.value = modMetadataRef.Attribution;
			if (modAttributionFieldChangeEvent != null)
				modAttributionField.UnregisterCallback(modAttributionFieldChangeEvent);
			modAttributionFieldChangeEvent = (evt) =>
			{
				if (!modPresetAttributions.choices.Contains(evt.newValue) || evt.newValue.Equals("Custom"))
				{
					modPresetAttributions.SetValueWithoutNotify(modPresetAttributions.choices[0]);
				}
				modMetadataRef.Attribution = evt.newValue;

				projectDatabaseManager.SaveAssetBundle(assetbundle);
			};
			modAttributionField.RegisterCallback(modAttributionFieldChangeEvent);

			// VERSION LIST
			modVersionList.bindItem = (element, index) => { };
			foreach (IntegerField integerField in modVersionListChangeEvents.Keys)
			{
				integerField.UnregisterCallback(modVersionListChangeEvents[integerField]);
			}
			modVersionListChangeEvents.Clear();
			modVersionListTargetOrder.Clear();
			modVersionList.Clear();
			modVersionListTargetOrder = SupportedTargets.Keys.ToList();
			modVersionList.itemsSource = SupportedTargets.Keys.Select(_ => 0).ToList();
			modVersionList.makeItem = () => new IntegerField();
			modVersionList.bindItem = (element, index) =>
			{
				IntegerField thisIntegerField = element as IntegerField;
				QueuedActions.Add(() =>
				{
					thisIntegerField.label = SupportedTargets[modVersionListTargetOrder[index]].Item2;
					thisIntegerField.value = modMetadataRef.BuildNumberMap[modVersionListTargetOrder[index].ToString()];
				});

				projectDatabaseManager.SaveAssetBundle(assetbundle);

				if (modVersionListChangeEvents.ContainsKey(thisIntegerField))
				{
					thisIntegerField.UnregisterCallback(modVersionListChangeEvents[thisIntegerField]);
				}

				EventCallback<ChangeEvent<int>> changeEventCallback = (evt) =>
				{
					modMetadataRef.BuildNumberMap[modVersionListTargetOrder[index].ToString()] = evt.newValue;

					projectDatabaseManager.SaveAssetBundle(assetbundle);
				};

				modVersionListChangeEvents[thisIntegerField] = changeEventCallback;

				thisIntegerField.RegisterCallback(changeEventCallback);
			};

			// CREATORS LIST
			modCreatorList.bindItem = (element, index) => { };
			modCreatorList.itemsAdded -= previousListAddedEvent;
			modCreatorList.itemsRemoved -= previousListRemovedEvent;
			foreach (TextField textField in modCreatorListChangeEvents.Keys)
			{
				textField.UnregisterCallback(modCreatorListChangeEvents[textField]);
			}
			modCreatorListChangeEvents.Clear();
			modCreatorList.Clear();
			modCreatorList.itemsSource = modMetadataRef.Creators.ToList();
			modCreatorList.makeItem = () => new TextField();
			modCreatorList.bindItem = (element, index) =>
			{
				TextField thisTextField = element as TextField;
				QueuedActions.Add(() =>
				{
					thisTextField.value = modMetadataRef.Creators[index];
				});

				projectDatabaseManager.SaveAssetBundle(assetbundle);

				if (modCreatorListChangeEvents.ContainsKey(thisTextField))
				{
					thisTextField.UnregisterCallback(modCreatorListChangeEvents[thisTextField]);
				}

				EventCallback<ChangeEvent<string>> changeEventCallback = (evt) =>
				{
					modMetadataRef.Creators[index] = evt.newValue;

					projectDatabaseManager.SaveAssetBundle(assetbundle);
				};

				modCreatorListChangeEvents[thisTextField] = changeEventCallback;

				thisTextField.RegisterCallback(changeEventCallback);
			};
			previousListAddedEvent = (indices) =>
			{
				foreach (int i in indices)
				{
					if (i >= modMetadataRef.Creators.Count)
					{
						modMetadataRef.Creators.Add("");

						projectDatabaseManager.SaveAssetBundle(assetbundle);
					}
				}
			};
			previousListRemovedEvent = (indices) =>
			{
				foreach (int i in indices)
				{
					if (i < modMetadataRef.Creators.Count)
					{
						modMetadataRef.Creators.RemoveAt(i);

						projectDatabaseManager.SaveAssetBundle(assetbundle);
					}
				}
			};
			modCreatorList.itemsAdded += previousListAddedEvent;
			modCreatorList.itemsRemoved += previousListRemovedEvent;

			// UUID
			QueuedActions.Add(() =>
			{
				modConfigBox.Q<TextField>("mod-uuid-field").value = $"{modMetadataRef.Uuid}";
			});

			actionsDone++;
			QueuedActions.Add(() =>
			{
				OnAssetBundleLoadProgress((int)Math.Floor((actionsDone / totalActions) * 100));
			});


			///
			/// STEP 4:
			/// Populate Asset List UI with data from the project database
			/// ----------------------------------------------------------------------------------

			// iterate through all assets
			foreach (Guid assetUuid in modMetadataRef.AssetMap.Keys)
			{
				QueuedActions.Add(() =>
				{
					ReloadAssetBundleModAssetUI(modMetadataRef, modExtraDataRef, assetUuid, ref actionsDone, totalActions);
				});
			}

			///
			/// STEP 5:
			/// Finalize asset filter UI and update progress

			QueuedActions.Add(() =>
			{
				// squash type list into unique types only
				assetTypeList = assetTypeList.Distinct().ToList();

				assetFilterField.choices = assetTypeList;
				assetFilterField.value = IntPow(2, assetTypeList.Count) - 1; // pow2 because bitmask
				foreach (EventCallback<ChangeEvent<int>> callback in assetFilterChangedCallbacks)
				{
					assetFilterField.RegisterCallback(callback);
				}

				OnAssetBundleLoadProgress(100);
			});
		}

		

		void ReloadAssetBundleModAssetUI(
			ModMetadata modMetadataRef,
			ModEditorData modExtraDataRef,
			Guid assetUuid,
			ref float actionsDone,
			float totalActions)
		{
			///
			/// STEP 4A:
			/// Setup asset element UI
			/// --------------------------------------------------------------------------------
			
			string assetPath = modMetadataRef.AssetMap[assetUuid];
			ExtraAssetSettings extraAssetSettings = modExtraDataRef.ExtraAssetSettings[assetUuid.ToString()];
			ModPrefab prefabData = modMetadataRef.PrefabMap.ContainsKey(assetUuid)
				? modMetadataRef.PrefabMap[assetUuid]
				: null;
			ExtraPrefabSettings extraPrefabSettings = modExtraDataRef.ExtraPrefabSettings.ContainsKey(assetUuid.ToString())
				? modExtraDataRef.ExtraPrefabSettings[assetUuid.ToString()]
				: null;
			
			var newListElement = new AssetListPrefabElement(
				assetUuid,
				assetPath,
				extraAssetSettings,
				prefabData,
				extraPrefabSettings
			); // inside AssetListPrefabElement constructor is the UI setup

			///
			/// STEP 4B:
			/// Setup callbacks for asset element UI
			/// --------------------------------------------------------------------------------


			newListElement.OnExtraAssetSettingsChanged += (newExtraAssetSettings) =>
			{
				modExtraDataRef.ExtraAssetSettings[assetUuid.ToString()] = newExtraAssetSettings;
			};
			newListElement.OnPrefabDataChanged += (newPrefabData) =>
			{
				if (newPrefabData != null)
				{
					if (modMetadataRef.PrefabMap.ContainsKey(assetUuid))
					{
						modMetadataRef.PrefabMap[assetUuid] = newPrefabData;
					}
					else
					{
						modMetadataRef.PrefabMap.Add(assetUuid, newPrefabData);
					}
				}
				else
				{
					modMetadataRef.PrefabMap.Remove(assetUuid);
				}
			};
			newListElement.OnExtraPrefabSettingsChanged += (newExtraPrefabSettings) =>
			{
				if (newExtraPrefabSettings != null)
				{
					if (modExtraDataRef.ExtraPrefabSettings.ContainsKey(assetUuid.ToString()))
					{
						modExtraDataRef.ExtraPrefabSettings[assetUuid.ToString()] = newExtraPrefabSettings;
					}
					else
					{
						modExtraDataRef.ExtraPrefabSettings.Add(assetUuid.ToString(), newExtraPrefabSettings);
					}
				}
				else
				{
					modExtraDataRef.ExtraPrefabSettings.Remove(assetUuid.ToString());
				}
			};


			///
			/// STEP 4C:
			/// Setup search, filter, and attribution callbacks for asset element UI
			/// --------------------------------------------------------------------------------


			/// TYPE LIST
			string assetType = AssetDatabase.GetMainAssetTypeAtPath(assetPath).ToString();
			assetTypeList.Add(assetType);

			// SEARCH
			EventCallback<ChangeEvent<string>> searchCallback = (ChangeEvent<string> changeEvent) =>
			{
				string newValue = changeEvent.newValue;
				QueuedActions.Add(() =>
				{
					SetVisible(newListElement, AssetFilterCheck(newValue, GenerateAssetSearchIndex(assetPath, assetUuid, prefabData), assetFilterField.value, assetType));
				});
			};
			assetSearchChangedCallbacks.Add(searchCallback);
			assetSearchField.RegisterCallback(searchCallback);

			// FILTER
			EventCallback<ChangeEvent<int>> filterCallback = (ChangeEvent<int> changeEvent) =>
			{
				int newValue = changeEvent.newValue;
				QueuedActions.Add(() =>
				{
					SetVisible(newListElement, AssetFilterCheck(assetSearchField.value, GenerateAssetSearchIndex(assetPath, assetUuid, prefabData), newValue, assetType));
				});
			};
			assetFilterChangedCallbacks.Add(filterCallback);

			// ATTRIBUTION
			EventCallback<ChangeEvent<string>> attributeCallback = (ChangeEvent<string> changeEvent) =>
			{
				string newValue = changeEvent.newValue;
				TextField attributionField = newListElement.Q<TextField>("menu-object-attribution-field");
				attributionField.value = newValue;
			};
			assetAttributionChangedCallback.Add(attributeCallback);
			modAttributionField.RegisterCallback(attributeCallback);


			///
			/// STEP 4D:
			/// Add the asset element to the scrollview and update progress
			/// --------------------------------------------------------------------------------


			assetsScrollview.Add(newListElement);
			actionsDone++;
			OnAssetBundleLoadProgress((int)Math.Floor(actionsDone / totalActions * 100));
		}

		bool AssetFilterCheck(string query, string searchIndex, int mask, string assetType)
		{
			bool searchMatch = query == "" || Fuzz.PartialTokenSetRatio(searchIndex.ToLower(), query.ToLower()) > 90;

			int check = assetTypeList.IndexOf(assetType);

			return searchMatch && ((mask >> check) & 0b1) != 0;
		}

		string GenerateAssetSearchIndex(string path, Guid guid, ModPrefab prefab)
		{
			string prefabIndex = "";

			if (prefab != null)
			{
				prefabIndex = $"{prefab.Category} {prefab.FormattedName}";
			}

			return $"{path} {guid} {prefabIndex}";
		}

		int IntPow(int x, int pow)
		{
			int ret = 1;
			while (pow != 0)
			{
				if ((pow & 1) == 1)
					ret *= x;
				x *= x;
				pow >>= 1;
			}
			return ret;
		}

		Box uploadQueue;
		Button closeUploads;
		ScrollView uploadsScrollview;
		Dictionary<(Guid, BuildTarget), ProgressBar> uploadProgressBars = new();
		Action closeUploadsEvent;

		void InitializeUploadQueueSection()
		{
			uploadQueue = UIRootVisualElement.Q<Box>("upload-queue");
			closeUploads = UIRootVisualElement.Q<Button>("close-uploads");
			uploadsScrollview = UIRootVisualElement.Q<ScrollView>("uploads-scrollview");

			// closeUploads.style.backgroundImage = new StyleBackground(EditorGUIUtility.FindTexture("Cancel"));
			QueuedActions.Add(() =>
			{
				SetVisible(closeUploads, false);
				SetVisible(uploadQueue, false);
			});

			repositoryManager.OnUploadQueueUpdate += UploadQueueUpdated;

			if (closeUploadsEvent != null)
				closeUploads.clicked -= closeUploadsEvent;
			closeUploadsEvent = () =>
			{
				QueuedActions.Add(() =>
				{
					SetVisible(uploadQueue, false);

					uploadsScrollview.Clear();
					uploadProgressBars.Clear();
				});
			};
			closeUploads.clicked += closeUploadsEvent;
		}

		void UploadQueueUpdated(List<(Guid, BuildTarget, int)> uploads)
		{
			bool shouldShowClose = true;
			foreach ((Guid, BuildTarget, int) upload in uploads)
			{
				if (upload.Item3 < 100 && upload.Item3 >= -1)
				{
					shouldShowClose = false;
				}

				if (!uploadProgressBars.ContainsKey((upload.Item1, upload.Item2)))
				{
					uploadProgressBars.Add((upload.Item1, upload.Item2), null);

					QueuedActions.Add(() =>
					{
						VisualTreeAsset newListElement = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
							$"Packages/{CollabModdingWindow.BundleID}/Editor/ModdingWindow/ModdingWindowUploadListElement.uxml"
						);
						VisualElement newListElementRendered = newListElement.Instantiate();

						ProgressBar progressBar = newListElementRendered.Q<ProgressBar>("upload-progress-bar");

						FormatProgressBarForUpload(progressBar, upload);

						uploadProgressBars[(upload.Item1, upload.Item2)] = progressBar;

						uploadsScrollview.Add(newListElementRendered);
					});
				}
				else
				{
					QueuedActions.Add(() =>
					{
						FormatProgressBarForUpload(uploadProgressBars[(upload.Item1, upload.Item2)], upload);
					});
				}
			}

			if (uploads.Count > 0)
			{
				QueuedActions.Add(() =>
				{
					SetVisible(uploadQueue, true);
				});
			}
			QueuedActions.Add(() =>
			{
				SetVisible(closeUploads, shouldShowClose);
			});
		}

		void FormatProgressBarForUpload(ProgressBar bar, (Guid, BuildTarget, int) upload)
		{
			if (bar == null)
				return;

			VisualElement background = bar.Q<VisualElement>(className: "unity-progress-bar__progress");

			string modName = upload.Item1.ToString();

			foreach (ModMetadata metadata in projectDatabaseManager.ProjectDatabase.AssetbundleToModMap.Values)
			{
				if (metadata.Uuid == upload.Item1)
				{
					modName = metadata.Name;
					break;
				}
			}

			QueuedActions.Add(() =>
			{
				if (upload.Item3 == -2)
				{
					bar.title = $"Upload {modName} for {upload.Item2} Failed";
					bar.value = 100;
					background.style.backgroundColor = new StyleColor(new Color(0.529411764706f, 0.172549019608f, 0.172549019608f, 1));
					// "#872C2C"
				}
				else if (upload.Item3 == -1)
				{
					bar.title = $"Upload {modName} for {upload.Item2} Pending...";
					bar.value = 0;
					background.style.backgroundColor = new StyleColor(new Color(0.172549019608f, 0.364705882353f, 0.529411764706f, 1));
					// "#2C5D87"
				}
				else if (upload.Item3 >= 100)
				{
					bar.title = $"Done Uploading {modName} for {upload.Item2}";
					bar.value = 100;
					background.style.backgroundColor = new StyleColor(new Color(0.172549019608f, 0.529411764706f, 0.172549019608f, 1));
					// "#2C872C"
				}
				else
				{
					bar.title = $"Uploading {modName} for {upload.Item2}...";
					bar.value = upload.Item3;
					background.style.backgroundColor = new StyleColor(new Color(0.172549019608f, 0.364705882353f, 0.529411764706f, 1));
					// "#2C5D87"
				}
			});
		}

		BuildTargetSelector buildTargetSelector;
		Action buildTargetSelectorAction;

		void InitializeCompileModSection()
		{
			buildTargetSelector = new BuildTargetSelector(SupportedTargets.Keys.Select(target => (target, SupportedTargets[target].Item2)));

			buildSelectorButton = UIRootVisualElement.Q<CustomPopupButton>("build-selector");
			buildButton = UIRootVisualElement.Q<Button>("build");
			buildPublishSelectorButton = UIRootVisualElement.Q<CustomPopupButton>("build-publish-selector");
			buildPublishButton = UIRootVisualElement.Q<Button>("build-publish");

			if (buildTargetSelectorAction != null)
				buildTargetSelector.onTargetsUpdated -= buildTargetSelectorAction;
			if (buildPublishButtonAction != null)
				buildPublishButton.clicked -= buildPublishButtonAction;

			buildTargetSelectorAction = () =>
			{
				QueuedActions.Add(() =>
				{
					buildButton.SetEnabled(buildButtonValidInContext && buildTargetSelector.IsAtLeastOneSelected());
					buildPublishButton.SetEnabled(buildButtonValidInContext && buildTargetSelector.IsAtLeastOneSelected());
				});

				string labelContents = buildTargetSelector.GetShortLabel();

				buildSelectorButton.Text = labelContents;
				buildPublishSelectorButton.Text = labelContents;
			};

			buildButtonAction = () =>
			{
				CompileMod(buildTargetSelector.EnabledTargets.Keys.Where(target => buildTargetSelector.EnabledTargets[target]).ToList(), false);
			};
			buildPublishButtonAction = () =>
			{
				CompileMod(buildTargetSelector.EnabledTargets.Keys.Where(target => buildTargetSelector.EnabledTargets[target]).ToList(), true);
			};

			buildSelectorButton.SetPopupWindowContent(buildTargetSelector);
			buildPublishSelectorButton.SetPopupWindowContent(buildTargetSelector);

			buildTargetSelector.onTargetsUpdated += buildTargetSelectorAction;
			buildButton.clicked += buildButtonAction;
			buildPublishButton.clicked += buildPublishButtonAction;
		}
	}
}
