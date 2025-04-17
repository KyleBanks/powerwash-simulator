using System;
using KBCore.Refs;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PaintableSurfaceUILineItem : MonoBehaviour
{
    [SerializeField, Child(Flag.Editable)] private TMP_Text _nameLabel;
    [SerializeField, Child(Flag.Editable)] private TMP_Text _progressLabel;
    [SerializeField, Child] private Slider _progressSlider;

    private Color _defaultTextColor;

    private void Awake()
    {
        this._defaultTextColor = this._nameLabel.color;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        this.ValidateRefs();
    }
#endif

    private void Update()
    {
        this._nameLabel.color = this._progressLabel.color =
            Color.Lerp(this._nameLabel.color, this._defaultTextColor, Time.deltaTime * 2);
    }

    public void Bind(PaintableSurface surface)
    {
        this.UpdateDisplay(surface);
        this._nameLabel.text = surface.gameObject.name;
        this._nameLabel.color = this._progressLabel.color = this._defaultTextColor;
        surface.OnDirtinessChanged += this.UpdateDisplay;
    }

    private void UpdateDisplay(PaintableSurface surface)
    {
        this._progressLabel.text = (surface.Cleanliness * 100).ToString("0.0") + "%";
        this._progressSlider.value = surface.Cleanliness;
        this._nameLabel.color = this._progressLabel.color = Color.green;
    }
}