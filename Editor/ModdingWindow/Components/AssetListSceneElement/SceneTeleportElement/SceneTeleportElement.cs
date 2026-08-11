using UnityEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using System;

namespace CollabXR.ModPackager
{
    [UxmlElement("scene-teleport-element")]
    public partial class SceneTeleportElement : VisualElement
    {
        private TextField teleportName;
        private Vector3Field teleportPosition;
        private Button deleteButton;

        public string TeleportName
        {
            get { return teleportName.value; }
            set
            {
                teleportName.value = value;

                CollabModdingWindow.QueuedActions.Add(() =>
                {
                    MarkDirtyRepaint();
                });
            }
        }

        public Vector3 TeleportPosition
        {
            get { return teleportPosition.value; }
            set
            {
                teleportPosition.value = value;

                CollabModdingWindow.QueuedActions.Add(() =>
                {
                    MarkDirtyRepaint();
                });
            }
        }

        public Action<string> OnDeleteButtonClicked;

        public SceneTeleportElement() {}

        public SceneTeleportElement(string name, Vector3 position)
        {
            VisualTreeAsset visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
				$"Packages/{CollabModdingWindow.BundleID}/Editor/ModdingWindow/Components/AssetListSceneElement/SceneTeleportElement/SceneTeleportElement.uxml"
			);

            visualTreeAsset.CloneTree(this);

            teleportName = this.Q<TextField>("teleport-name");
            teleportPosition = this.Q<Vector3Field>("teleport-position");

            deleteButton = this.Q<Button>("delete-teleport-button");
            deleteButton.RegisterCallback<ClickEvent>(evt =>
            {
                OnDeleteButtonClicked?.Invoke(teleportName.value);
            });
        }
    }
}