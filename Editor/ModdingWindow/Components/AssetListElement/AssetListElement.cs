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
	public partial class AssetListElement : VisualElement
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

		private Vector3Field menuObjectMinScale;
		private Vector3Field menuObjectMaxScale;
		private Vector3Field menuObjectStartOffset;

		private Toggle menuObjectThumbnailAutoGenerate;
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

					UnityEngine.Object assetPrefab = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
					Editor newEditor = Editor.CreateEditor(assetPrefab);
					Texture2D previewTexture = newEditor.RenderStaticPreview(assetPath, null, 64, 64);
					EditorWindow.DestroyImmediate(newEditor);

					assetPreview.image = previewTexture;

					UpdatePrefabUI();

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

		public ModPrefab PrefabData
		{
			get { return prefabData; }
			set
			{
				prefabData = value;

				UpdatePrefabUI();
			}
		}
		public Action<ModPrefab> OnPrefabDataChanged;
		private ModPrefab prefabData;

		public ExtraPrefabSettings ExtraPrefabSettings
		{
			get { return extraPrefabSettings; }
			set
			{
				extraPrefabSettings = value;

				UpdatePrefabUI();
			}
		}
		public Action<ExtraPrefabSettings> OnExtraPrefabSettingsChanged;
		private ExtraPrefabSettings extraPrefabSettings;

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

		private void UpdatePrefabUI()
		{
			CollabModdingWindow.QueuedActions.Add(() =>
			{
				if (AssetDatabase.GetMainAssetTypeAtPath(assetPath) == typeof(GameObject))
				{
					bool isPrefab = prefabData != null && extraPrefabSettings != null;

					SetVisible(menuObjectToggle, true);
					menuObjectToggle.SetValueWithoutNotify(isPrefab);

					SetVisible(menuObjectSettings, isPrefab);

					if (isPrefab)
					{
						menuObjectCategoryField.SetValueWithoutNotify(prefabData.Category);
						menuObjectFormattedNameField.SetValueWithoutNotify(prefabData.FormattedName);
						menuObjectAttributionField.SetValueWithoutNotify(prefabData.Attribution);
						menuObjectAttributionPreset.choices = PresetAttributions;
						menuObjectAttributionPreset.SetValueWithoutNotify((menuObjectAttributionPreset.choices.Count > 0) ? menuObjectAttributionPreset.choices[0] : "");
						menuObjectMinScale.SetValueWithoutNotify(prefabData.MinScale);
						menuObjectMaxScale.SetValueWithoutNotify(prefabData.MaxScale);
						menuObjectStartOffset.SetValueWithoutNotify(prefabData.StartingOffset);

						SetVisible(menuObjectThumbnailSelect, !extraPrefabSettings.AutoGenerate);

						menuObjectThumbnailAutoGenerate.SetValueWithoutNotify(extraPrefabSettings.AutoGenerate);
						menuObjectThumbnailSelect.SetValueWithoutNotify(extraPrefabSettings.Texture);

						if (extraPrefabSettings.Texture != null)
						{
							string texturePath = AssetDatabase.GetAssetPath(extraPrefabSettings.Texture);

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
					SetVisible(menuObjectToggle, false);
					SetVisible(menuObjectSettings, false);
				}

				MarkDirtyRepaint();
			});
		}

		public AssetListElement() { }

		public AssetListElement(Guid newAssetUuid, string newAssetPath, ExtraAssetSettings newExtraAssetSettings, ModPrefab newPrefabData, ExtraPrefabSettings newExtraPrefabSettings)
		{
			VisualTreeAsset visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
				$"Packages/{CollabModdingWindow.BundleID}/Editor/ModdingWindow/Components/AssetListElement/AssetListElement.uxml"
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

			menuObjectMinScale = this.Q<Vector3Field>("menu-object-min-scale");
			menuObjectMaxScale = this.Q<Vector3Field>("menu-object-max-scale");
			menuObjectStartOffset = this.Q<Vector3Field>("menu-object-start-offset");

			menuObjectThumbnailAutoGenerate = this.Q<Toggle>("menu-object-thumbnail-auto-generate");
			menuObjectThumbnailSelect = this.Q<ObjectField>("menu-object-thumbnail-select");
			thumbnailImportError = this.Q<HelpBox>("thumbnail-import-error");
			autoFixThumbnailImportButton = this.Q<Button>("autofix-thumbnail-import");

			AssetUuid = newAssetUuid;
			AssetPath = newAssetPath;

			ExtraAssetSettings = newExtraAssetSettings;

			PrefabData = newPrefabData;
			ExtraPrefabSettings = newExtraPrefabSettings;

			menuObjectToggle.RegisterCallback<ChangeEvent<bool>>(
				(changeEvent) =>
				{
					bool updatedPrefabData = false;
					bool updatedExtraPrefabSettings = false;
					if (changeEvent.newValue)
					{
						if (prefabData == null)
						{
							prefabData = new();
							updatedPrefabData = true;
						}

						if (extraPrefabSettings == null)
						{
							extraPrefabSettings = new();
							updatedExtraPrefabSettings = true;
						}
					}
					else
					{
						prefabData = null;
						updatedPrefabData = true;
					}

					UpdatePrefabUI();

					if (updatedPrefabData)
						OnPrefabDataChanged?.Invoke(prefabData);
					if (updatedExtraPrefabSettings)
						OnExtraPrefabSettingsChanged?.Invoke(extraPrefabSettings);
				}
			);

			menuObjectCategoryField.RegisterCallback<ChangeEvent<string>>(
				(changeEvent) =>
				{
					if (prefabData != null)
					{
						prefabData.Category = changeEvent.newValue;

						OnPrefabDataChanged?.Invoke(prefabData);
					}
				}
			);

			menuObjectFormattedNameField.RegisterCallback<ChangeEvent<string>>(
				(changeEvent) =>
				{
					if (prefabData != null)
					{
						prefabData.FormattedName = changeEvent.newValue;

						OnPrefabDataChanged?.Invoke(prefabData);
					}
				}
			);

			menuObjectAttributionPreset.RegisterCallback<ChangeEvent<string>>(
				(changeEvent) =>
				{
					if (prefabData != null && menuObjectAttributionField != null)
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
					if (prefabData != null)
					{
						if (!menuObjectAttributionPreset.choices.Contains(changeEvent.newValue) || changeEvent.newValue.Equals("Custom"))
						{
							menuObjectAttributionPreset.SetValueWithoutNotify((menuObjectAttributionPreset.choices.Count > 0) ? menuObjectAttributionPreset.choices[0] : "");
						}

						prefabData.Attribution = changeEvent.newValue;

						OnPrefabDataChanged?.Invoke(prefabData);
					}
				}
			);

			menuObjectMinScale.RegisterCallback<ChangeEvent<Vector3>>(
				(changeEvent) =>
				{
					if (prefabData != null)
					{
						prefabData.MinScale = changeEvent.newValue;

						OnPrefabDataChanged?.Invoke(prefabData);
					}
				}
			);

			menuObjectMaxScale.RegisterCallback<ChangeEvent<Vector3>>(
				(changeEvent) =>
				{
					if (prefabData != null)
					{
						prefabData.MaxScale = changeEvent.newValue;

						OnPrefabDataChanged?.Invoke(prefabData);
					}
				}
			);

			menuObjectStartOffset.RegisterCallback<ChangeEvent<Vector3>>(
				(changeEvent) =>
				{
					if (prefabData != null)
					{
						prefabData.StartingOffset = changeEvent.newValue;

						OnPrefabDataChanged?.Invoke(prefabData);
					}
				}
			);

			menuObjectThumbnailAutoGenerate.RegisterCallback<ChangeEvent<bool>>(
				(ChangeEvent<bool> changeEvent) =>
				{
					if (extraPrefabSettings != null)
					{
						extraPrefabSettings.AutoGenerate = changeEvent.newValue;

						UpdatePrefabUI();

						OnExtraPrefabSettingsChanged?.Invoke(extraPrefabSettings);
					}
				}
			);

			menuObjectThumbnailSelect.RegisterCallback<ChangeEvent<UnityEngine.Object>>(
				(changeEvent) =>
				{
					if (extraPrefabSettings != null)
					{
						extraPrefabSettings.Texture = (Texture2D)changeEvent.newValue;

						UpdatePrefabUI();

						OnExtraPrefabSettingsChanged?.Invoke(extraPrefabSettings);
					}
				}
			);

			autoFixThumbnailImportButton.clicked += () =>
			{
				if (extraPrefabSettings != null)
				{
					string texturePath = AssetDatabase.GetAssetPath(extraPrefabSettings.Texture);

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
