using System;
using System.Collections.Generic;
using Unslop.UnityBridge.Editor.Materials;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unslop.UnityBridge.Editor.UI
{
    /// <summary>
    /// UI Toolkit rows for material conflict resolution choices.
    /// </summary>
    public sealed class MaterialReviewElement : VisualElement
    {
        readonly Dictionary<string, MaterialResolution> _choices = new Dictionary<string, MaterialResolution>(StringComparer.Ordinal);
        readonly VisualElement _rows;
        readonly Label _summary;
        MaterialOwnershipReport _report;

        public event Action<MaterialConflict, MaterialResolution> ResolutionChosen;
        public event Action ApplyAllClicked;

        public IReadOnlyDictionary<string, MaterialResolution> Choices => _choices;

        public MaterialReviewElement()
        {
            style.flexDirection = FlexDirection.Column;
            style.marginTop = 6;

            _summary = new Label("No material conflicts.")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 4 }
            };
            Add(_summary);

            _rows = new VisualElement { style = { flexDirection = FlexDirection.Column } };
            Add(_rows);

            var apply = new Button(() => ApplyAllClicked?.Invoke()) { text = "Apply Selected Resolutions" };
            apply.style.marginTop = 6;
            Add(apply);
        }

        public void Bind(MaterialOwnershipReport report)
        {
            _report = report;
            _rows.Clear();
            _choices.Clear();

            if (report?.Conflicts == null || report.Conflicts.Count == 0)
            {
                _summary.text = "No material conflicts.";
                return;
            }

            _summary.text = $"{report.Conflicts.Count} material conflict(s) for {report.AssetId}";
            foreach (var conflict in report.Conflicts)
            {
                _choices[conflict.SlotId] = conflict.SuggestedResolution;
                _rows.Add(BuildRow(conflict));
            }
        }

        VisualElement BuildRow(MaterialConflict conflict)
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginBottom = 4,
                    paddingTop = 2,
                    paddingBottom = 2,
                    borderBottomWidth = 1,
                    borderBottomColor = new Color(0.25f, 0.25f, 0.25f)
                }
            };

            var label = new Label(Describe(conflict))
            {
                style =
                {
                    flexGrow = 1,
                    whiteSpace = WhiteSpace.Normal,
                    minWidth = 200
                }
            };
            row.Add(label);

            var dropdown = new DropdownField(
                new List<string>
                {
                    "Accept remote",
                    "Keep local",
                    "Textures only",
                    "Properties only",
                    "Remap to project material"
                },
                ToIndex(conflict.SuggestedResolution));
            dropdown.style.minWidth = 180;
            dropdown.RegisterValueChangedCallback(evt =>
            {
                var resolution = FromLabel(evt.newValue);
                _choices[conflict.SlotId] = resolution;
                ResolutionChosen?.Invoke(conflict, resolution);
            });
            row.Add(dropdown);
            return row;
        }

        static string Describe(MaterialConflict conflict)
        {
            var flags = new List<string>();
            if (conflict.IsLocalOverride)
            {
                flags.Add("local_override");
            }

            if (conflict.IsLocallyModified)
            {
                flags.Add("modified");
            }

            if (conflict.IsOrphan)
            {
                flags.Add("orphan");
            }

            if (conflict.IsRemovedFromRemote)
            {
                flags.Add("removed");
            }

            var flagText = flags.Count == 0 ? "new" : string.Join(", ", flags);
            return $"{conflict.SlotId}  [{flagText}]\n{conflict.MaterialPath ?? "(no path)"}";
        }

        static int ToIndex(MaterialResolution resolution) => resolution switch
        {
            MaterialResolution.AcceptRemote => 0,
            MaterialResolution.KeepLocal => 1,
            MaterialResolution.TexturesOnly => 2,
            MaterialResolution.PropertiesOnly => 3,
            MaterialResolution.RemapToProjectMaterial => 4,
            _ => 1
        };

        static MaterialResolution FromLabel(string label) => label switch
        {
            "Accept remote" => MaterialResolution.AcceptRemote,
            "Keep local" => MaterialResolution.KeepLocal,
            "Textures only" => MaterialResolution.TexturesOnly,
            "Properties only" => MaterialResolution.PropertiesOnly,
            "Remap to project material" => MaterialResolution.RemapToProjectMaterial,
            _ => MaterialResolution.KeepLocal
        };
    }
}
