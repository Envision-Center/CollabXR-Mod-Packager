using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CollabXR.ModPackager
{
	[UxmlElement]
	public partial class AssetListSceneElement : VisualElement
	{
		public static readonly List<string> PresetAttributions = new() // list of commonly used attributions for ease of access
		{
			"Custom",
			"Envision Center",
			"Smithsonian",
		};

		private Label assetTitle;
		private TextField assetUuidField;
		private Image assetPreview;

		private Toggle menuObjectToggle;
		private Box menuObjectSettings;

		private TextField menuObjectCategoryField;
		private TextField menuObjectFormattedNameField;

		private TextField menuObjectAttributionField;
		private DropdownField menuObjectAttributionPreset;

		private ObjectField menuObjectThumbnailSelect;
		private HelpBox thumbnailImportError;
		private Button autoFixThumbnailImportButton;

		public Guid AssetUuid
		{
			get { return assetUuid; }
			set
			{
				assetUuid = value;

				CollabModdingWindow.QueuedActions.Add(() =>
				{
					assetUuidField.value = $"{assetUuid.ToString()}";

					MarkDirtyRepaint();
				});
			}
		}
		private Guid assetUuid;

		public string AssetPath
		{
			get { return assetPath; }
			set
			{
				assetPath = value;

				CollabModdingWindow.QueuedActions.Add(() =>
				{
					assetTitle.text = $"<b>{Path.GetFileName(assetPath)}</b> ({assetPath})";
					assetPreview.image = extraSceneSettings?.Texture != null 
						? extraSceneSettings.Texture 
						: AssetPreview.GetAssetPreview(AssetDatabase.LoadAssetAtPath<SceneAsset>(assetPath));
					MarkDirtyRepaint();
				});
			}
		}
		private string assetPath;

		public ExtraAssetSettings ExtraAssetSettings
		{
			get { return extraAssetSettings; }
			set { extraAssetSettings = value; }
		}
		public Action<ExtraAssetSettings> OnExtraAssetSettingsChanged;
		private ExtraAssetSettings extraAssetSettings;

		public ModScene SceneData
		{
			get { return sceneData; }
			set
			{
				sceneData = value;

				UpdateSceneUI();
			}
		}
		public Action<ModScene> OnSceneDataChanged;
		private ModScene sceneData;

		public ExtraSceneSettings ExtraSceneSettings
		{
			get { return extraSceneSettings; }
			set
			{
				extraSceneSettings = value;

				UpdateSceneUI();
			}
		}
		public Action<ExtraSceneSettings> OnExtraSceneSettingsChanged;
		private ExtraSceneSettings extraSceneSettings;

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

		bool CheckIfThumbnailTextureNeedsCorrection(string path)
		{
			TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);

			if (!importer.isReadable)
				return true;

			if (importer.textureType != TextureImporterType.Default)
				return true;

			return false;
		}

		void CorrectThumbnailTexture(string path)
		{
			TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);

			importer.textureType = TextureImporterType.Default;
			importer.isReadable = true;

			importer.SaveAndReimport();
		}

		private void UpdateSceneUI()
		{
			CollabModdingWindow.QueuedActions.Add(() =>
			{
				if (AssetDatabase.GetMainAssetTypeAtPath(assetPath) == typeof(SceneAsset))
				{
					bool isScene = sceneData != null && extraSceneSettings != null;
					Logger.Info($"Updating scene UI for {assetPath}. Is scene: {isScene}");

					SetVisible(menuObjectSettings, isScene);

					if (isScene)
					{
						menuObjectCategoryField.SetValueWithoutNotify(sceneData.Category);
						menuObjectFormattedNameField.SetValueWithoutNotify(sceneData.FormattedName);
						menuObjectAttributionField.SetValueWithoutNotify(sceneData.Attribution);
						menuObjectAttributionPreset.choices = PresetAttributions;
						menuObjectAttributionPreset.SetValueWithoutNotify((menuObjectAttributionPreset.choices.Count > 0) ? menuObjectAttributionPreset.choices[0] : "");

						menuObjectThumbnailSelect.SetValueWithoutNotify(extraSceneSettings.Texture);

						if (extraSceneSettings.Texture != null)
						{
							string texturePath = AssetDatabase.GetAssetPath(extraSceneSettings.Texture);

							SetVisible(thumbnailImportError, CheckIfThumbnailTextureNeedsCorrection(texturePath));
						}
						else
						{
							SetVisible(thumbnailImportError, false);
						}
					}
				}
				else
				{
					SetVisible(menuObjectSettings, false);
				}

				MarkDirtyRepaint();
			});
		}

		public AssetListSceneElement() { }

		public AssetListSceneElement(Guid newAssetUuid, string newAssetPath, ExtraAssetSettings newExtraAssetSettings, ModScene newSceneData, ExtraSceneSettings newExtraSceneSettings)
		{
			VisualTreeAsset visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
				$"Packages/{CollabModdingWindow.BundleID}/Editor/ModdingWindow/Components/AssetListSceneElement/AssetListSceneElement.uxml"
			);
			visualTreeAsset.CloneTree(this);

			style.flexGrow = 1;

			assetTitle = this.Q<Label>("asset-title");
			assetUuidField = this.Q<TextField>("asset-uuid");
			assetPreview = this.Q<Image>("asset-preview");
			menuObjectToggle = this.Q<Toggle>("asset-menu-object");

			menuObjectSettings = this.Q<Box>("menu-object-settings");

			menuObjectCategoryField = this.Q<TextField>("menu-object-category");
			menuObjectFormattedNameField = this.Q<TextField>("menu-object-formatted-name");

			menuObjectAttributionField = this.Q<TextField>("menu-object-attribution-field");
			menuObjectAttributionPreset = this.Q<DropdownField>("menu-object-preset-attributions");

			menuObjectThumbnailSelect = this.Q<ObjectField>("menu-object-thumbnail-select");
			thumbnailImportError = this.Q<HelpBox>("thumbnail-import-error");
			autoFixThumbnailImportButton = this.Q<Button>("autofix-thumbnail-import");

			AssetUuid = newAssetUuid;
			AssetPath = newAssetPath;

			ExtraAssetSettings = newExtraAssetSettings;

			SceneData = newSceneData;
			ExtraSceneSettings = newExtraSceneSettings;

			Logger.Info($"AssetListSceneElement created for {assetPath} with UUID {assetUuid}. Is scene: {sceneData != null && extraSceneSettings != null}");

			menuObjectToggle.RegisterCallback<ChangeEvent<bool>>(
				(changeEvent) =>
				{
					bool updatedSceneData = false;
					bool updatedExtraSceneSettings = false;
					if (changeEvent.newValue)
					{
						if (sceneData == null)
						{
							sceneData = new();
							updatedSceneData = true;
						}

						if (extraSceneSettings == null)
						{
							extraSceneSettings = new();
							updatedExtraSceneSettings = true;
						}
					}
					else
					{
						sceneData = null;
						updatedSceneData = true;
					}

					UpdateSceneUI();

					if (updatedSceneData)
						OnSceneDataChanged?.Invoke(sceneData);
					if (updatedExtraSceneSettings)
						OnExtraSceneSettingsChanged?.Invoke(extraSceneSettings);
				}
			);

			menuObjectCategoryField.RegisterCallback<ChangeEvent<string>>(
				(changeEvent) =>
				{
					if (sceneData != null)
					{
						sceneData.Category = changeEvent.newValue;

						OnSceneDataChanged?.Invoke(sceneData);
					}
				}
			);

			menuObjectFormattedNameField.RegisterCallback<ChangeEvent<string>>(
				(changeEvent) =>
				{
					if (sceneData != null)
					{
						sceneData.FormattedName = changeEvent.newValue;

						OnSceneDataChanged?.Invoke(sceneData);
					}
				}
			);

			menuObjectAttributionPreset.RegisterCallback<ChangeEvent<string>>(
				(changeEvent) =>
				{
					if (sceneData != null && menuObjectAttributionField != null)
					{
						if (changeEvent.newValue.Equals("Custom"))
							return;

						menuObjectAttributionField.value = changeEvent.newValue;
					}
				}
			);

			menuObjectAttributionField.RegisterCallback<ChangeEvent<string>>(
				(changeEvent) =>
				{
					if (sceneData != null)
					{
						if (!menuObjectAttributionPreset.choices.Contains(changeEvent.newValue) || changeEvent.newValue.Equals("Custom"))
						{
							menuObjectAttributionPreset.SetValueWithoutNotify((menuObjectAttributionPreset.choices.Count > 0) ? menuObjectAttributionPreset.choices[0] : "");
						}

						sceneData.Attribution = changeEvent.newValue;

						OnSceneDataChanged?.Invoke(sceneData);
					}
				}
			);

			menuObjectThumbnailSelect.RegisterCallback<ChangeEvent<UnityEngine.Object>>(
				(changeEvent) =>
				{
					if (extraSceneSettings != null)
					{
						extraSceneSettings.Texture = (Texture2D)changeEvent.newValue;

						UpdateSceneUI();

						OnExtraSceneSettingsChanged?.Invoke(extraSceneSettings);
					}
				}
			);

			autoFixThumbnailImportButton.clicked += () =>
			{
				if (extraSceneSettings != null)
				{
					string texturePath = AssetDatabase.GetAssetPath(extraSceneSettings.Texture);

					CorrectThumbnailTexture(texturePath);

					CollabModdingWindow.QueuedActions.Add(() =>
					{
						SetVisible(thumbnailImportError, false);

						MarkDirtyRepaint();
					});
				}
			};
		}
	}
}
