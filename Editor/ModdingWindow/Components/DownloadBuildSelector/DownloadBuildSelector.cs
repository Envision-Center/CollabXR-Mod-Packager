using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CollabXR.ModPackager
{
	public class DownloadBuildSelector : PopupWindowContent
	{
		public Dictionary<BuildTarget, bool> AvailableBuilds = new();
		Dictionary<BuildTarget, string> targetNames = new();

		float windowHeight = 0;

		public Action<BuildTarget> onDownloadBuild;

		public DownloadBuildSelector(Dictionary<BuildTarget, bool> newAvailableBuilds)
		{
			this.AvailableBuilds = newAvailableBuilds;

			windowHeight = (3 * 2) + (this.AvailableBuilds.Count * 23);
		}

		public override Vector2 GetWindowSize()
		{
			return new Vector2(200, windowHeight);
		}

		public override void OnGUI(Rect rect)
		{
			// Intentionally left empty
		}

		Dictionary<BuildTarget, (Button, Action)> downloadCallbacks = new();
		VisualElement UIRootVisualElement;
		Box buttonContainer;

		public override void OnOpen()
		{
			VisualTreeAsset mainUI = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
				$"Packages/{CollabModdingWindow.BundleID}/Editor/ModdingWindow/Components/DownloadBuildSelector/DownloadBuildSelector.uxml"
			);

			UIRootVisualElement = mainUI.Instantiate();
			UIRootVisualElement.style.flexGrow = 1;

			buttonContainer = UIRootVisualElement.Q<Box>("button-container");

			foreach (BuildTarget target in AvailableBuilds.Keys)
			{
				VisualTreeAsset downloadEntryUI = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
					$"Packages/{CollabModdingWindow.BundleID}/Editor/ModdingWindow/Components/DownloadBuildSelector/DownloadBuildSelectorButton.uxml"
				);
				VisualElement downloadEntry = downloadEntryUI.Instantiate();

				Button downloadButton = downloadEntry.Q<Button>("download-button");
				downloadButton.text = CollabModdingWindow.SupportedTargets[target].Item2;
				downloadButton.SetEnabled(AvailableBuilds[target]);

				Action downloadCallback = () =>
				{
					onDownloadBuild?.Invoke(target);
				};

				downloadButton.clicked += downloadCallback;
				downloadCallbacks.Add(target, (downloadButton, downloadCallback));

				buttonContainer.Add(downloadEntry);
			}

			editorWindow.rootVisualElement.Add(UIRootVisualElement);
		}

		public override void OnClose()
		{
			foreach (BuildTarget target in downloadCallbacks.Keys)
			{
				downloadCallbacks[target].Item1.clicked -= downloadCallbacks[target].Item2;
			}

			downloadCallbacks.Clear();
		}
	}
}
