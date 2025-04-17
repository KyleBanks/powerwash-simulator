using KBCore.Refs;
using UnityEngine;

public class Painter : MonoBehaviour
{

    private static readonly Collider[] COLLIDERS = new Collider[100];

    public float SpreadRadius = 0.1f;
    public float Range = 10;
    public LayerMask LayerMask;

    [SerializeField, Self] private Transform _transform;

    private void Update()
    {
        bool fire1 = Input.GetButton("Fire1");
        bool fire2 = Input.GetButton("Fire2");
        if (!fire1 && !fire2)
            return;
    
        this._transform.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
        
        if (!Physics.Raycast(position, rotation * Vector3.forward, out RaycastHit hit, this.Range, this.LayerMask))
            return;
        
        int hits = Physics.OverlapSphereNonAlloc(hit.point, this.SpreadRadius, COLLIDERS, this.LayerMask);
        for (int i = 0; i < hits; i++)
        {
            Collider col = COLLIDERS[i];
            if (!col.TryGetComponent(out PaintableSurface surface))
                continue;

            Vector3 normal = rotation * Vector3.back;
            Color color = fire2 ? Color.white : Color.black;
            surface.Paint(hit.point, normal, this.SpreadRadius, color);
        }
    }


#if UNITY_EDITOR
    private void OnValidate()
    {
        this.ValidateRefs();
    }
#endif
}