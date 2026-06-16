using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CollabXR.ModPackager
{
	[UxmlElement]
	public partial class CustomPopupButton : VisualElement
	{
		private Label popupLabel;

		[SerializeField]
		[UxmlAttribute("text")]
		public string Text
		{
			get { return text; }
			set
			{
				text = value;

				popupLabel.text = text;
			}
		}
		private string text = "";

		private PopupWindowContent popupWindowContent;

		private void onClick()
		{
			if (enabledSelf)
				UnityEditor.PopupWindow.Show(worldBound, popupWindowContent);
		}

		public void SetPopupWindowContent(PopupWindowContent PopupWindowContent)
		{
			popupWindowContent = PopupWindowContent;
		}

		public CustomPopupButton()
		{
			VisualTreeAsset visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
				$"Packages/{CollabModdingWindow.BundleID}/Editor/ModdingWindow/Components/CustomPopupButton/CustomPopupButton.uxml"
			);
			visualTreeAsset.CloneTree(this);

			AddToClassList("unity-base-field");
			AddToClassList("unity-base-popup-field");
			AddToClassList("unity-popup-field");

			popupLabel = this.Q<Label>(className: "unity-text-element");

			RegisterCallback<ClickEvent>(
				(_) =>
				{
					onClick();
				}
			);
		}

		public CustomPopupButton(PopupWindowContent PopupWindowContent, string newText)
		{
			VisualTreeAsset visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
				$"Packages/{CollabModdingWindow.BundleID}/Editor/ModdingWindow/Components/CustomPopupButton/CustomPopupButton.uxml"
			);
			visualTreeAsset.CloneTree(this);

			AddToClassList("unity-base-field");
			AddToClassList("unity-base-popup-field");
			AddToClassList("unity-popup-field");

			popupLabel = this.Q<Label>(className: "unity-text-element");

			RegisterCallback<ClickEvent>(
				(_) =>
				{
					onClick();
				}
			);

			SetPopupWindowContent(PopupWindowContent);
			Text = newText;
		}
	}
}
