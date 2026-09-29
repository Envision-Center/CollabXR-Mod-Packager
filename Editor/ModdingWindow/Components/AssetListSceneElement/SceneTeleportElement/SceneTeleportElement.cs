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
        ///
        /// UI Level data
        /// --------------------------
        public TextField teleportName;
        public Vector3Field teleportPosition;
        public Button deleteButton;
        
        ///
        /// Internal actual data
        /// --------------------------
        private ModScene sceneDataRef;
        private string _teleportName;
        private Vector3 _teleportPosition;

        public string TeleportName
        {
            get => _teleportName;
            set
            {
                _teleportName = value;
                teleportName.value = value;
            }
        }
        
        public Vector3 TeleportPosition
        {
            get => _teleportPosition;
            set
            {
                _teleportPosition = value;
                teleportPosition.value = value;
            }
        }

        public Action<string> OnDeleteButtonClicked;

        public SceneTeleportElement() {}

        public SceneTeleportElement(string name, Vector3 position, ModScene sceneData)
        {
            this.sceneDataRef = sceneData;

            VisualTreeAsset visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
				$"Packages/{CollabModdingWindow.BundleID}/Editor/ModdingWindow/Components/AssetListSceneElement/SceneTeleportElement/SceneTeleportElement.uxml"
			);

            visualTreeAsset.CloneTree(this);

            teleportName = this.Q<TextField>("teleport-name");
            teleportPosition = this.Q<Vector3Field>("teleport-position");
            
            TeleportName = name;
            TeleportPosition = position;
            
            deleteButton = this.Q<Button>("delete-teleport-button");
        }
    }
}