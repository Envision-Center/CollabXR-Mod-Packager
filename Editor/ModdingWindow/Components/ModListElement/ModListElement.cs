using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CollabXR.ModPackager
{
	[UxmlElement]
	public partial class ModListElement : VisualElement
	{
		private Label modTitle;
		private TextField modOwner;
		private TextField modUuid;

		private ListView modVersionList;
		private List<BuildTarget> modVersionListTargetOrder = new();
		private ListView modCreatorList;

		private Button downloadMetadata;
		private CustomPopupButton downloadBuild;
		private DownloadBuildSelector downloadBuildSelector = new(new Dictionary<BuildTarget, bool>());

		private Label deleteWarning;
		private ProgressBar deleteProgress;
		private Button deleteButton;
		private Button deleteConfirmButton;
		private Button deleteDenyButton;

		public string RepositoryURL
		{
			get { return repositoryURL; }
			set { repositoryURL = value; }
		}
		private string repositoryURL;

		public ModMetadata Metadata
		{
			get { return metadata; }
			set
			{
				metadata = value;

				CollabModdingWindow.QueuedActions.Add(() =>
				{
					modTitle.text = $"<b>{metadata.Name}</b>";
					modOwner.value = $"{metadata.Owner}";
					modUuid.value = $"{metadata.Uuid}";

					modVersionList.Clear();
					modVersionListTargetOrder.Clear();
					modVersionListTargetOrder = CollabModdingWindow.SupportedTargets.Keys.ToList();
					modVersionList.itemsSource = CollabModdingWindow.SupportedTargets.Keys.Select(_ => 0).ToList();
					modVersionList.makeItem = () => new IntegerField();
					modVersionList.bindItem = (element, index) =>
					{
						IntegerField thisIntegerField = element as IntegerField;
						CollabModdingWindow.QueuedActions.Add(() =>
						{
							thisIntegerField.isReadOnly = true;
							thisIntegerField.label = CollabModdingWindow.SupportedTargets[modVersionListTargetOrder[index]].Item2;
							if (metadata.BuildNumberMap.ContainsKey(modVersionListTargetOrder[index].ToString()))
								thisIntegerField.value = metadata.BuildNumberMap[modVersionListTargetOrder[index].ToString()];
							else
								thisIntegerField.value = 0;
						});
					};
					modVersionList.Q<Foldout>().value = false;

					modCreatorList.Clear();
					modCreatorList.itemsSource = metadata.Creators;
					modCreatorList.makeItem = () => new TextField();
					modCreatorList.bindItem = (element, index) =>
					{
						TextField thisTextField = element as TextField;
						CollabModdingWindow.QueuedActions.Add(() =>
						{
							thisTextField.isReadOnly = true;
							thisTextField.value = metadata.Creators[index];
						});
					};
					modCreatorList.Q<Foldout>().value = false;

					MarkDirtyRepaint();
				});
			}
		}
		private ModMetadata metadata;

		public Dictionary<BuildTarget, bool> AvailableTargets
		{
			get { return availableTargets; }
			set
			{
				availableTargets = value;

				CollabModdingWindow.QueuedActions.Add(() =>
				{
					downloadBuild.SetEnabled(availableTargets.Count != 0);

					downloadBuildSelector = new DownloadBuildSelector(availableTargets);
					downloadBuildSelector.onDownloadBuild += (target) =>
					{
						DownloadFile(target.ToString());
					};
					downloadBuild.SetPopupWindowContent(downloadBuildSelector);

					MarkDirtyRepaint();
				});
			}
		}
		private Dictionary<BuildTarget, bool> availableTargets;

		public Action onDelete;

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

		void FormatProgressBar(ProgressBar bar, int progress)
		{
			if (bar == null)
				return;

			VisualElement title = bar.Q<VisualElement>(className: "unity-progress-bar__title");
			VisualElement background = bar.Q<VisualElement>(className: "unity-progress-bar__progress");

			CollabModdingWindow.QueuedActions.Add(() =>
			{
				title.style.paddingLeft = 8;
				title.style.paddingRight = 8;

				if (progress == -2)
				{
					bar.title = $"Delete Failed";
					bar.value = 100;
					background.style.backgroundColor = new StyleColor(new Color(0.529411764706f, 0.172549019608f, 0.172549019608f, 1));
					// "#872C2C"
				}
				else if (progress == -1)
				{
					bar.title = $"Delete Pending...";
					bar.value = 0;
					background.style.backgroundColor = new StyleColor(new Color(0.172549019608f, 0.364705882353f, 0.529411764706f, 1));
					// "#2C5D87"
				}
				else if (progress >= 100)
				{
					bar.title = $"Done Deleting";
					bar.value = 100;
					background.style.backgroundColor = new StyleColor(new Color(0.172549019608f, 0.529411764706f, 0.172549019608f, 1));
					// "#2C872C"
				}
				else
				{
					bar.title = $"Deleting...";
					bar.value = progress;
					background.style.backgroundColor = new StyleColor(new Color(0.172549019608f, 0.364705882353f, 0.529411764706f, 1));
					// "#2C5D87"
				}
			});
		}

		void DownloadFile(string file)
		{
			Application.OpenURL($"{repositoryURL}{metadata.Uuid}.{file}");
		}

		public void UpdateDeleteionProgress(int progress)
		{
			CollabModdingWindow.QueuedActions.Add(() =>
			{
				FormatProgressBar(deleteProgress, progress);
			});
		}

		public ModListElement() { }

		public ModListElement(string newRepositoryURL, ModMetadata newMetadata, Dictionary<BuildTarget, bool> newAvailableTargets)
		{
			VisualTreeAsset visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
				$"Packages/{CollabModdingWindow.BundleID}/Editor/ModdingWindow/Components/ModListElement/ModListElement.uxml"
			);
			visualTreeAsset.CloneTree(this);

			modTitle = this.Q<Label>("mod-title");
			modOwner = this.Q<TextField>("mod-owner");
			modUuid = this.Q<TextField>("mod-uuid");

			modVersionList = this.Q<ListView>("mod-versions-list");
			modCreatorList = this.Q<ListView>("mod-creators-list");

			downloadMetadata = this.Q<Button>("download-meta-button");
			downloadBuild = this.Q<CustomPopupButton>("download-build-button");

			deleteWarning = this.Q<Label>("delete-warning");
			deleteProgress = this.Q<ProgressBar>("delete-progress");
			deleteButton = this.Q<Button>("delete-mod-button");
			deleteConfirmButton = this.Q<Button>("delete-confirm-button");
			deleteDenyButton = this.Q<Button>("delete-deny-button");

			RepositoryURL = newRepositoryURL;
			Metadata = newMetadata;
			AvailableTargets = newAvailableTargets;

			downloadMetadata.clicked += () =>
			{
				DownloadFile("meta.json");
			};
			downloadBuild.SetPopupWindowContent(downloadBuildSelector);

			deleteButton.clicked += () =>
			{
				CollabModdingWindow.QueuedActions.Add(() =>
				{
					SetVisible(deleteButton, false);
					SetVisible(deleteWarning, true);
					SetVisible(deleteConfirmButton, true);
					SetVisible(deleteDenyButton, true);
					SetVisible(deleteProgress, false);
				});
			};
			deleteConfirmButton.clicked += () =>
			{
				CollabModdingWindow.QueuedActions.Add(() =>
				{
					SetVisible(deleteButton, false);
					SetVisible(deleteWarning, false);
					SetVisible(deleteConfirmButton, false);
					SetVisible(deleteDenyButton, false);
					SetVisible(deleteProgress, true);
				});

				onDelete?.Invoke();
			};
			deleteDenyButton.clicked += () =>
			{
				CollabModdingWindow.QueuedActions.Add(() =>
				{
					SetVisible(deleteButton, true);
					SetVisible(deleteWarning, false);
					SetVisible(deleteConfirmButton, false);
					SetVisible(deleteDenyButton, false);
					SetVisible(deleteProgress, false);
				});
			};

			CollabModdingWindow.QueuedActions.Add(() =>
			{
				FormatProgressBar(deleteProgress, -1);

				SetVisible(deleteButton, true);
				SetVisible(deleteWarning, false);
				SetVisible(deleteConfirmButton, false);
				SetVisible(deleteDenyButton, false);
				SetVisible(deleteProgress, false);
			});
		}
	}
}
