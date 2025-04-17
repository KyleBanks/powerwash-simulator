using System;
using KBCore.Refs;
using UnityEngine;

public class PaintableSurfaceUI : MonoBehaviour
{

    [SerializeField, Scene] private PaintableSurface[] _surfaces;
    [SerializeField, Anywhere] private PaintableSurfaceUILineItem _lineItemPrefab;

    private void Start()
    {
        for (int i = 0; i < this._surfaces.Length; i++)
        {
            PaintableSurfaceUILineItem lineItem = Instantiate(this._lineItemPrefab, this.transform);
            lineItem.Bind(this._surfaces[i]);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        this.ValidateRefs();
    }
#endif
}