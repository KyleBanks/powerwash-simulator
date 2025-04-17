using System;
using System.Collections.Generic;
using KBCore.Refs;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class PaintableSurface : MonoBehaviour
{
    private static readonly List<Material> MATERIAL_CACHE = new();
    private static readonly Vector2[] COVERAGE_RESULT_BUFFER = new Vector2[1];
    
    private static readonly int SHADER_PARAM_PAINT_POSITION_WS = Shader.PropertyToID("_PaintPositionWS");
    private static readonly int SHADER_PARAM_PAINT_NORMAL_WS = Shader.PropertyToID("_PaintNormalWS");
    private static readonly int SHADER_PARAM_SPREAD_RADIUS = Shader.PropertyToID("_SpreadRadius");
    private static readonly int SHADER_PARAM_PAINT_COLOR = Shader.PropertyToID("_PaintColor");
    
    private static readonly int SHADER_PARAM_DIRT_MASK = Shader.PropertyToID("_DirtMask");
    private static readonly int SHADER_PARAM_PREVIOUS_FRAME_TEX = Shader.PropertyToID("_PreviousFrameTex");
    
    private static readonly int SHADER_PARAM_INPUT_TEXTURE = Shader.PropertyToID("InputTexture");
    private static readonly int SHADER_PARAM_INPUT_BUFFER = Shader.PropertyToID("InputBuffer");
    private static readonly int SHADER_PARAM_OUTPUT_BUFFER = Shader.PropertyToID("OutputBuffer");

    public enum TextureSizes
    {
        XS = 64,
        S = XS * 2,
        M = S * 2,
        L = M * 2,
        XL = L * 2,
        XXL = XL * 2,
    }

    public event Action<PaintableSurface> OnDirtinessChanged;

    public float Dirtiness { get; private set; } = 1;
    public float Cleanliness { get; private set; } = 0;
    
    public TextureSizes TextureSize = TextureSizes.L;
    public Shader PaintShader;
    public ComputeShader CoverageShader;
    
    [SerializeField, Self] private Transform _transform;
    [SerializeField, Child(Flag.Editable)] private MeshRenderer _mainRenderer;

    private bool _hasSetup;
    private RenderTexture _rt;
    private CommandBuffer _cmd;
    private readonly Dictionary<Material, Material> _materials = new();
    private Material _paintMaterial;    
    private LocalKeyword _floorColorKeyword;
    private int _coverageKernel;
    private int _coverageGroups;
    private uint _coverageThreadGroupSize;
    private ComputeBuffer[] _coverageBuffers = new ComputeBuffer[2];
    private LocalKeyword _coverageShaderTextureModeKeyword;
    
    private void OnDestroy()
    {
        if (this._hasSetup)
        {
            this._rt.Release();
            Destroy(this._rt);
        }
        
        foreach (KeyValuePair<Material, Material> kvp in this._materials)
            Destroy(kvp.Value);
        if (this._paintMaterial)
            Destroy(this._paintMaterial);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        this.ValidateRefs();
        ValidateRenderer(this._mainRenderer);
    }

    private static void ValidateRenderer(Renderer rend)
    {
        StaticEditorFlags staticFlags = GameObjectUtility.GetStaticEditorFlags(rend.gameObject);
        if ((staticFlags & StaticEditorFlags.BatchingStatic) > 0)
        {
            string warning = $"{rend.name} has StaticBatching enabled, must be disabled for painting to work";
            if (Application.isPlaying)
            {
                Debug.LogError(warning, rend.gameObject);
            }
            else
            {
                Debug.LogWarning(warning, rend.gameObject);
                
                staticFlags &= ~StaticEditorFlags.BatchingStatic;
                GameObjectUtility.SetStaticEditorFlags(rend.gameObject, staticFlags);
                EditorUtility.SetDirty(rend.gameObject);
            }
        }
    }
#endif
    
    internal void Paint(
        Vector3 position,
        Vector3 normal,
        float radius, 
        Color color
    )
    {
        this.Setup();
        this._paintMaterial.SetKeyword(this._floorColorKeyword, false);
        this._paintMaterial.SetVector(SHADER_PARAM_PAINT_POSITION_WS, position);
        this._paintMaterial.SetVector(SHADER_PARAM_PAINT_NORMAL_WS, normal);
        this._paintMaterial.SetFloat(SHADER_PARAM_SPREAD_RADIUS, radius);
        this._paintMaterial.SetColor(SHADER_PARAM_PAINT_COLOR, color);
        Graphics.ExecuteCommandBuffer(this._cmd);
        this.MeasureCoverage();
    }

    private void FloodColor(Color color)
    {
        this._paintMaterial.SetKeyword(this._floorColorKeyword, true);
        this._paintMaterial.SetColor(SHADER_PARAM_PAINT_COLOR, color);
        Graphics.ExecuteCommandBuffer(this._cmd);
    }

    [ContextMenu("Setup")]
    private void Setup()
    {
        if (this._hasSetup)
        {
            // check if RT data has been lost
            if (!this._rt.IsCreated())
            {
                Debug.LogWarning($"{this.name} recreating render texture", this.gameObject);
                this._rt.Create();
            }
            return;
        }
        this._hasSetup = true;

        this._paintMaterial = new Material(this.PaintShader);
        this._floorColorKeyword = new LocalKeyword(this._paintMaterial.shader, "FLOOD_COLOR");

        this._rt = new RenderTexture((int) this.TextureSize, (int) this.TextureSize, 0, RenderTextureFormat.ARGB32)
        {
            enableRandomWrite = true,
            wrapMode = TextureWrapMode.Clamp
        };
        this._rt.Create();
        
        RenderTexture temp = RenderTexture.active;
        RenderTexture.active = this._rt;
        GL.Clear(true, true, new Color(1, 1, 1, 0));
        RenderTexture.active = temp;

        int mainRendererMaterialCount = this.SetupRenderer(this._mainRenderer);

        this._cmd = CommandBufferPool.Get("Paint Surface");
        this._cmd.GetTemporaryRT(SHADER_PARAM_PREVIOUS_FRAME_TEX, this._rt.descriptor);
        this._cmd.Blit(this._rt, SHADER_PARAM_PREVIOUS_FRAME_TEX);
		this._cmd.SetRenderTarget(this._rt);
        this._cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
        for (int i = 0; i < mainRendererMaterialCount; i++)
            this._cmd.DrawRenderer(this._mainRenderer, this._paintMaterial, i, 0);
        this._cmd.ReleaseTemporaryRT(SHADER_PARAM_PREVIOUS_FRAME_TEX);
        
        this._coverageKernel = this.CoverageShader.FindKernel("Reduce");
        this.CoverageShader.GetKernelThreadGroupSizes(this._coverageKernel, out this._coverageThreadGroupSize, out uint _, out uint _);   
        this._coverageShaderTextureModeKeyword = new LocalKeyword(this.CoverageShader, "TEXTURE_MODE");
        
        this._coverageGroups = Mathf.CeilToInt(this._rt.width * this._rt.height / (float) this._coverageThreadGroupSize);
        // TODO: need break this up into batches for large textures while avoiding exceeding the max group size
        this._coverageGroups = Mathf.Min(this._coverageGroups, 65535);

        this._coverageBuffers[0] = new ComputeBuffer(this._coverageGroups, sizeof(float) * 2);
        this._coverageBuffers[1] = new ComputeBuffer(this._coverageGroups, sizeof(float) * 2); 
        
        this.FloodColor(Color.white);
    }

    private int SetupRenderer(Renderer rend)
    {
        try
        {
            rend.GetSharedMaterials(MATERIAL_CACHE);
            for (int i = 0; i < MATERIAL_CACHE.Count; i++)
            {
                Material material = MATERIAL_CACHE[i];
                if (!this._materials.TryGetValue(material, out Material remappedMaterial))
                {
                    remappedMaterial = new Material(material);
                    remappedMaterial.SetTexture(SHADER_PARAM_DIRT_MASK, this._rt);
                    this._materials[material] = remappedMaterial;
                }
                MATERIAL_CACHE[i] = remappedMaterial;
            }
            rend.SetSharedMaterials(MATERIAL_CACHE);
            return MATERIAL_CACHE.Count;
        }
        finally
        {
            MATERIAL_CACHE.Clear();
        }
    }

    private void MeasureCoverage()
    {
        // Texture -> Buffer
        int groups = this._coverageGroups;
        this.CoverageShader.EnableKeyword(this._coverageShaderTextureModeKeyword);
        this.CoverageShader.SetTexture(this._coverageKernel, SHADER_PARAM_INPUT_TEXTURE, this._rt);
        this.CoverageShader.SetBuffer(this._coverageKernel, SHADER_PARAM_OUTPUT_BUFFER, this._coverageBuffers[0]);
        this.CoverageShader.Dispatch(this._coverageKernel, groups, 1, 1);
        this.CoverageShader.DisableKeyword(this._coverageShaderTextureModeKeyword);

        // Reduce buffers
        bool bufferToggle = false;
        while (groups > 1)
        {
            groups = Mathf.CeilToInt(groups / (float)this._coverageThreadGroupSize);
            this.CoverageShader.SetBuffer(this._coverageKernel, SHADER_PARAM_INPUT_BUFFER, this._coverageBuffers[bufferToggle ? 1 : 0]);
            this.CoverageShader.SetBuffer(this._coverageKernel, SHADER_PARAM_OUTPUT_BUFFER, this._coverageBuffers[bufferToggle ? 0 : 1]);
            this.CoverageShader.Dispatch(this._coverageKernel, groups, 1, 1);
            bufferToggle = !bufferToggle;
        }

        // Read final result
        ComputeBuffer final = this._coverageBuffers[bufferToggle ? 1 : 0];
        final.GetData(COVERAGE_RESULT_BUFFER);

        // Calculate dirtiness
        Vector2 output = COVERAGE_RESULT_BUFFER[0];
        float sumDirtiness = output.x;
        float validPixelCount = output.y;
        this.Dirtiness = Mathf.Clamp01(sumDirtiness / validPixelCount);
        // add a bit of leeway
        if (this.Dirtiness < 0.02f)
            this.Dirtiness = 0f;
        this.Cleanliness = 1 - this.Dirtiness;

        // Notify
        this.OnDirtinessChanged?.Invoke(this);
    }

}