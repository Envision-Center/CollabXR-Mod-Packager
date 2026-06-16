using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CollabXR.ModPackager
{
	public class BuildTargetSelector : PopupWindowContent
	{
		public Dictionary<BuildTarget, bool> EnabledTargets = new();

		float windowHeight = 0;

		public Action onTargetsUpdated;

		public bool IsAtLeastOneSelected()
		{
			foreach (bool targetEnabled in EnabledTargets.Values)
			{
				if (targetEnabled)
					return true;
			}

			return false;
		}

		public string GetShortLabel()
		{
			List<string> targets = EnabledTargets.Keys.Where(target => EnabledTargets[target]).Select(target => CollabModdingWindow.SupportedTargets[target].Item2).ToList();

			if (targets.Count == 0)
			{
				return "No Targets Selected";
			}
			else if (targets.Count >= EnabledTargets.Count)
			{
				return "All Targets";
			}
			else if (targets.Count == 1)
			{
				return targets[0];
			}
			else if (targets.Count == 2)
			{
				return $"{targets[0]} and {targets[1]}";
			}

			return $"{targets.Count} Targets";
		}

		public BuildTargetSelector(IEnumerable<(BuildTarget, string)> SupportedTargets)
		{
			EnabledTargets.Clear();

			foreach ((BuildTarget, string) target in SupportedTargets)
			{
				EnabledTargets.Add(target.Item1, false);
			}

			windowHeight = (3 * 2) + (EnabledTargets.Count * 19);
		}

		public override Vector2 GetWindowSize()
		{
			return new Vector2(200, windowHeight);
		}

		public override void OnGUI(Rect rect)
		{
			// Intentionally left empty
		}

		Dictionary<BuildTarget, (Toggle, EventCallback<ChangeEvent<bool>>)> toggleCallbacks = new();
		VisualElement UIRootVisualElement;
		Box toggleContainer;

		public override void OnOpen()
		{
			VisualTreeAsset mainUI = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
				$"Packages/{CollabModdingWindow.BundleID}/Editor/ModdingWindow/Components/BuildTargetSelector/BuildTargetSelector.uxml"
			);

			UIRootVisualElement = mainUI.Instantiate();
			UIRootVisualElement.style.flexGrow = 1;

			toggleContainer = UIRootVisualElement.Q<Box>("toggle-container");

			foreach (BuildTarget target in EnabledTargets.Keys)
			{
				VisualTreeAsset toggleEntryUI = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
					$"Packages/{CollabModdingWindow.BundleID}/Editor/ModdingWindow/Components/BuildTargetSelector/BuildTargetSelectorToggle.uxml"
				);
				VisualElement toggleEntry = toggleEntryUI.Instantiate();

				Toggle targetToggle = toggleEntry.Q<Toggle>("target-toggle");
				targetToggle.value = EnabledTargets[target];
				EventCallback<ChangeEvent<bool>> toggleCallback = (e) =>
				{
					EnabledTargets[target] = e.newValue;

					onTargetsUpdated?.Invoke();
				};

				targetToggle.RegisterValueChangedCallback(toggleCallback);
				toggleCallbacks.Add(target, (targetToggle, toggleCallback));

				toggleEntry.Q<Label>("target-label").text = CollabModdingWindow.SupportedTargets[target].Item2;

				toggleContainer.Add(toggleEntry);
			}

			editorWindow.rootVisualElement.Add(UIRootVisualElement);
		}

		public override void OnClose()
		{
			foreach (BuildTarget target in toggleCallbacks.Keys)
			{
				toggleCallbacks[target].Item1.UnregisterValueChangedCallback(toggleCallbacks[target].Item2);
				EnabledTargets[target] = toggleCallbacks[target].Item1.value;
			}

			toggleCallbacks.Clear();

			onTargetsUpdated?.Invoke();
		}
	}
}
