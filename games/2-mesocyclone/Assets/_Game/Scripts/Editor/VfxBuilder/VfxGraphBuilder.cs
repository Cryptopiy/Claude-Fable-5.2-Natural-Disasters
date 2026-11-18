// VfxGraphBuilder.cs - proposed, UNCOMPILED. Written against com.unity.visualeffectgraph 17.6.0 source.
// MUST live in an asmdef whose "name" is "Unity.VisualEffectGraph.EditorTests"
//   (InternalsVisibleTo: VFX/Editor/PackageInfo.cs:3, VFX/Runtime/PackageInfo.cs:3, engine VFX modules).
// Citations: VFX/ = Library/PackageCache/com.unity.visualeffectgraph@47051adff7cc/, HDRP/ = ...high-definition@03bdf33c5697/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.VFX;                       // VFXGraph, VFXContext, VFXParameter, VFXSlot, AABox, BoundsSettingMode ...
using UnityEngine;
using UnityEngine.VFX;                       // VisualEffectAsset, VFXSpace, VFXCullingFlags, VFXUpdateMode, VFXExposedProperty
using VfxBlocks = UnityEditor.VFX.Block;     // CustomHLSL (block), Orient, HLSLFunction
// CAUTION: UnityEditor.VFX also declares structs named Plane, Sphere, Line, Circle, Transform, Position, Vector ...
// (VFX/Editor/Types/VFXTypes.cs). Do not use those unqualified UnityEngine names in this file.

namespace MESOCYCLONE.EditorTools
{
    // Value names are parsed into the internal enums by name; keep spelling identical.
    public enum VfxBlend { Additive, Alpha, AlphaPremultiplied, Opaque }                     // VFXAbstractRenderedOutput.BlendMode (VFXAbstractRenderedOutput.cs:13-19)
    public enum VfxSort { Auto, Off, On }                                                    // VFXAbstractParticleOutput.SortActivationMode (:62-67)
    public enum VfxPrimitive { Triangle, Quad, Octagon }                                     // VFXPrimitiveType (VFXPlanarPrimitiveHelper.cs:9-14)
    public enum VfxOrient { None, FaceCameraPlane, FaceCameraPosition, LookAtPosition, FixedAxis, AlongVelocity, Advanced } // Block.Orient.Mode (Orient.cs:56-67)
    public enum VfxQueue { Default, BeforeRefraction, LowResolution, AfterPostProcessing }   // HDRenderQueue.TransparentRenderQueue (HDRP/Runtime/RenderPipeline/HDRenderQueue.cs:221-227)

    public sealed class OutputOptions
    {
        public bool lit = false;                               // false: VFXPlanarPrimitiveOutput (unlit). true: HDRP VFXLitPlanarPrimitiveOutput
        public string litMaterialType = "SimpleLit";           // HDRP MaterialType name (VFXAbstractParticleHDRPLitOutput.cs:14-22)
        public VfxBlend blend = VfxBlend.AlphaPremultiplied;
        public VfxSort sort = VfxSort.Off;                     // Auto == On for Alpha/AlphaPremultiplied (VFXAbstractParticleOutput.cs:171)
        public VfxPrimitive primitive = VfxPrimitive.Quad;
        public VfxOrient orient = VfxOrient.FaceCameraPlane;
        public VfxQueue queue = VfxQueue.Default;              // LowResolution needs HDRP asset lowresTransparentSettings.enabled + frame setting LowResTransparent
        public bool softParticles = true;
        public float softParticleFadeDistance = 2f;
        public bool useExposureWeight = true;                  // unlit output otherwise bypasses exposure (HDRP/Runtime/VFXGraph/Shaders/VFXCommon.hlsl:225-241)
        public float exposureWeight = 1f;
        public bool excludeFromTUAndAA = false;
        public bool generateMotionVector = false;
        public bool castShadows = false;
        public bool frustumCulling = false;
        public bool computeCulling = false;
        public bool indirectDraw = false;
        public Texture2D mainTexture;                          // must be a persistent asset; null keeps the package default particle texture
        public string mainTextureExposedName;                  // if set: link the exposed Texture2D parameter of that name to the output texture slot
        public bool worldSpace = false;                        // false = Local (recommended, see notes D/G)
        public Vector3 boundsCenter = Vector3.zero;            // local space (VFXBasicInitialize.cs:187-192)
        public Vector3 boundsSize = new Vector3(20000f, 20000f, 20000f);
        public bool hlslAsIncludeFile = true;                  // true: write <asset>.hlsl and reference it (helper functions allowed). false: embedded code
        public bool hlslInOutput = false;                      // true: no Update context; block runs in the output (vertex stage / output-update compute)
        public string blockName = "Stateless";
        public bool deltaTimeUpdate = true;                    // VFXUpdateMode.DeltaTime: Update runs once per rendered frame
    }

    public static class VfxGraphBuilder
    {
        /// exposed: (name, type, default). Convention: an exposed parameter named X is linked to the HLSL function parameter named X
        /// (block input slot "_X"). Supported types = types the HLSL parser maps (HLSLParser.cs:462-481): float, Vector2, Vector3,
        /// Vector4, Matrix4x4, bool, uint, int, Texture2D (VFXSampler2D), Texture3D (VFXSampler3D), Gradient, AnimationCurve.
        public static VisualEffectAsset BuildStateless(string assetPath, int capacity, string hlslFunctionSource,
            (string name, Type type, object def)[] exposed, OutputOptions opts)
        {
            if (opts == null) opts = new OutputOptions();
            if (exposed == null) exposed = Array.Empty<(string, Type, object)>();
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (string.IsNullOrEmpty(hlslFunctionSource)) throw new ArgumentException("empty HLSL", nameof(hlslFunctionSource));
            assetPath = assetPath.Replace('\\', '/');
            string hlsl = hlslFunctionSource.Replace("\r\n", "\n").Replace('\r', '\n');     // parser regexes are line based (HLSLParser.cs:247-248)

            // ---- 0. pre-flight ---------------------------------------------------------------------------------------
            // No supported SRP => compile returns success=false with no log (VFX/Editor/Compiler/VFXGraphCompiledData.cs:1187),
            // and outputs would get a generic sub-output instead of VFXHDRPSubOutput (VFXAbstractRenderedOutput.cs:130-150).
            if (VFXLibrary.currentSRPBinder == null)                                         // VFX/Editor/Core/VFXLibrary.cs:607-622
                throw new InvalidOperationException("VfxGraphBuilder: no HDRP asset is active (GraphicsSettings/QualitySettings). Assign it first.");
            VFXManagerEditor.CheckVFXManager();                                              // VFX/Editor/Inspector/VFXManagerEditor.cs:171-198

            string dir = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir))
            {
                Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            // ---- 1. empty asset -> resource -> graph ---------------------------------------------------------------------
            if (File.Exists(assetPath))
                VisualEffectResource.ForgetAtPath(assetPath);                                // usage: VFX/Editor/Models/VFXGraph.cs:65, VFXView.cs:210
            VisualEffectAsset asset = VisualEffectAssetEditorUtility.CreateNewAsset(assetPath); // VFX/Editor/VFXAssetEditorUtility.cs:103-126 (keeps existing .meta)
            if (asset == null) throw new InvalidOperationException("CreateNewAsset failed: " + assetPath);
            VisualEffectResource resource = asset.GetResource();                             // VFXGraph.cs:478-487
            VFXGraph graph = resource.GetGraph();                                            // VFXGraph.cs:374-379
            if (graph == null) throw new InvalidOperationException("VisualEffectResource has no graph: " + assetPath);

            // ---- 2. contexts ---------------------------------------------------------------------------------------------
            var spawn = ScriptableObject.CreateInstance<VFXBasicSpawner>();                  // VFXBasicSpawner.cs:170
            var init = ScriptableObject.CreateInstance<VFXBasicInitialize>();                // VFXBasicInitialize.cs:29
            VFXBasicUpdate update = opts.hlslInOutput ? null : ScriptableObject.CreateInstance<VFXBasicUpdate>(); // VFXBasicUpdate.cs:121
            VFXContext output = opts.lit
                ? CreateHdrpContext("UnityEditor.VFX.HDRP.VFXLitPlanarPrimitiveOutput")       // HDRP/Editor/VFXGraph/Outputs/VFXLitPlanarPrimitiveOutput.cs:49
                : ScriptableObject.CreateInstance<VFXPlanarPrimitiveOutput>();               // VFXPlanarPrimitiveOutput.cs:47

            graph.AddChild(spawn);                                                           // VFXModel.cs:148
            graph.AddChild(init);
            if (update != null) graph.AddChild(update);
            graph.AddChild(output);
            spawn.position = new Vector2(0, 0);                                              // VFXModel.cs:228 (UI only)
            init.position = new Vector2(0, 300);
            if (update != null) update.position = new Vector2(0, 700);
            output.position = new Vector2(0, 1100);

            spawn.LinkTo(init);                                                              // VFXContext.cs:329 ; data object is shared downstream (:454-455)
            if (update != null) { init.LinkTo(update); update.LinkTo(output); }
            else init.LinkTo(output);

            // ---- 3. spawn: one burst of N -------------------------------------------------------------------------------
            var burst = ScriptableObject.CreateInstance<VFXSpawnerBurst>();                  // VFXSpawnerBurst.cs:25 (defaults: repeat=Single, constant modes)
            spawn.AddChild(burst);
            SetSlot(burst, "Count", (float)capacity, true);                                  // VFXSpawnerBurst.cs:60-66
            SetSlot(burst, "Delay", 0f, true);

            // ---- 4. particle data: capacity, manual bounds, space -------------------------------------------------------
            init.SetSettingValue("capacity", (uint)capacity);                                // bridge to data: VFXBasicInitialize.cs:229-232 ; field VFXDataParticle.cs:247 (uint!)
            init.SetSettingValue("boundsMode", BoundsSettingMode.Manual);                    // VFXDataParticle.cs:215-220,266
            init.space = opts.worldSpace ? VFXSpace.World : VFXSpace.Local;                  // VFXContext.cs:761-784
            init.ResyncSlots(true);                                                          // VFXSlotContainerModel.cs:250
            SetSlot(init, "bounds", new AABox { center = opts.boundsCenter, size = opts.boundsSize }, true); // VFXBasicInitialize.cs:60-65 ; VFXTypes.cs:176-184
            // No lifetime attribute is written anywhere => no Age/Reap implicit blocks => particles are immortal (VFXBasicUpdate.cs:224-250).

            // ---- 5. output settings (before blocks, so slots exist) -----------------------------------------------------
            SetEnum(output, "blendMode", opts.blend.ToString());                             // VFXAbstractRenderedOutput.cs:46-47
            SetEnum(output, "cullMode", "Off");                                              // :49-50
            SetEnum(output, "zWriteMode", "Off");                                            // :52-53
            SetEnum(output, "primitiveType", opts.primitive.ToString());                     // VFXPlanarPrimitiveOutput.cs:49-50 / HDRP lit :57-58
            SetEnum(output, "uvMode", "Default");                                            // VFXAbstractParticleOutput.cs:95-96
            SetEnum(output, "sort", opts.sort.ToString());                                   // :113-114
            output.SetSettingValue("useSoftParticle", opts.softParticles);                   // :107-108
            output.SetSettingValue("castShadows", opts.castShadows);                         // :131-132
            output.SetSettingValue("useExposureWeight", opts.useExposureWeight);             // :134-135
            output.SetSettingValue("frustumCulling", opts.frustumCulling);                   // :128-129
            output.SetSettingValue("computeCulling", opts.computeCulling);                   // :125-126
            output.SetSettingValue("indirectDraw", opts.indirectDraw);                       // :122-123
            output.SetSettingValue("generateMotionVector", opts.generateMotionVector);       // VFXAbstractRenderedOutput.cs:61-62
            output.SetSettingValue("excludeFromTUAndAA", opts.excludeFromTUAndAA);           // :64-65
            // HDRP sub-output setting, reached through the GetSetting fallback (VFXAbstractRenderedOutput.cs:217-222).
            SetEnum(output, "transparentRenderQueue", opts.queue.ToString());                // HDRP/Editor/VFXGraph/VFXHDRPSubOutput.cs:17-18
            if (opts.lit)
            {
                SetEnum(output, "materialType", opts.litMaterialType);                       // VFXAbstractParticleHDRPLitOutput.cs:47-48
                SetEnum(output, "colorMode", "BaseColor");                                   // VFXAbstractParticleHDRPOutput.cs:30-31
                output.SetSettingValue("doubleSided", true);                                 // VFXAbstractParticleHDRPLitOutput.cs:59-60
            }
            output.ResyncSlots(true);
            if (opts.softParticles) SetSlot(output, "softParticleFadeDistance", opts.softParticleFadeDistance, false); // VFXAbstractParticleOutput.cs:461-462
            if (opts.useExposureWeight) SetSlot(output, "exposureWeight", opts.exposureWeight, false);                  // :464-465
            string texSlot = opts.lit ? "baseColorMap" : "mainTexture";                      // HDRP VFXAbstractParticleHDRPOutput.cs:44 / VFXPlanarPrimitiveOutput.cs:89
            if (opts.mainTexture != null) SetSlot(output, texSlot, opts.mainTexture, false);

            // ---- 6. orientation block (output only: Orient.cs:120) ------------------------------------------------------
            if (opts.orient != VfxOrient.None)
            {
                var orient = ScriptableObject.CreateInstance<VfxBlocks.Orient>();            // Orient.cs:54
                output.AddChild(orient);
                SetEnum(orient, "mode", opts.orient.ToString());                             // Orient.cs:79-80
            }

            // ---- 7. Custom HLSL block -----------------------------------------------------------------------------------
            // Pre-flight with the package's own parser so mistakes surface here, not as a silent empty block.
            var functions = VfxBlocks.HLSLFunction.Parse(graph.attributesManager, hlsl).ToList();   // HLSLParser.cs:251 ; VFXGraph.cs:556
            if (functions.Count == 0)
                throw new InvalidOperationException("Custom HLSL: no function recognised. Signature must start at column 0: 'void Name(inout VFXAttributes attributes, in <type> p, ...)'.");
            var fn = functions[0];                                                           // block selects the first non-hidden function by default (CustomHLSL.cs:328-341)
            foreach (var e in fn.errorList)                                                  // HLSLParser.cs:301
                Debug.LogError("Custom HLSL '" + fn.name + "': " + e.message);
            if (fn.errorList.Count > 0) throw new InvalidOperationException("Custom HLSL attribute errors, see log.");
            Debug.Log("Custom HLSL '" + fn.name + "' attributes: " +
                      string.Join(", ", fn.attributes.Select(a => a.attrib.name + ":" + a.mode)));   // HLSLParser.cs:298

            var block = ScriptableObject.CreateInstance<VfxBlocks.CustomHLSL>();             // CustomHLSL.cs:58
            VFXContext host = update != null ? (VFXContext)update : output;
            host.AddChild(block);                                                            // OnAdded -> reparse now that a graph exists (CustomHLSL.cs:259-264,298-304)
            block.SetSettingValue("m_BlockName", opts.blockName);                            // CustomHLSL.cs:78
            if (opts.hlslAsIncludeFile)
            {
                string hlslPath = Path.ChangeExtension(assetPath, ".hlsl");
                File.WriteAllText(hlslPath, hlsl);
                AssetDatabase.ImportAsset(hlslPath, ImportAssetOptions.ForceUpdate);
                var include = AssetDatabase.LoadAssetAtPath<ShaderInclude>(hlslPath);        // .hlsl imports as ShaderInclude (VFXAssetEditorUtility.cs:159)
                if (include == null) throw new InvalidOperationException("Could not load ShaderInclude " + hlslPath);
                block.SetSettingValue("m_ShaderFile", include);                              // CustomHLSL.cs:85 (embedded code is then ignored, :287-296)
            }
            else
            {
                block.SetSettingValue("m_HLSLCode", hlsl);                                   // CustomHLSL.cs:82
            }
            block.ResyncSlots(true);                                                         // CustomHLSL.cs:253-257
            int expectedInputs = fn.inputs.Count(i => i.type != null && i.type != typeof(VFXAttribute)); // CustomHLSL.cs:350-356
            if (block.inputSlots.Count != expectedInputs)
                throw new InvalidOperationException("Custom HLSL block has " + block.inputSlots.Count + " input slots, expected " + expectedInputs);

            // ---- 8. exposed parameters, linked by name ------------------------------------------------------------------
            var byName = new Dictionary<string, VFXParameter>();
            int order = 0;
            foreach (var e in exposed)
            {
                var p = ScriptableObject.CreateInstance<VFXParameter>();                     // VFXLibrary.cs:165-171
                p.Init(e.type);                                                              // VFXParameter.cs:471-487
                graph.AddChild(p);
                p.SetSettingValue("m_ExposedName", e.name);                                  // VFXParameter.cs:56-57 ; VFXViewController.cs:1249
                p.SetSettingValue("m_Exposed", true);                                        // VFXParameter.cs:58-59
                p.order = order++;                                                           // VFXParameter.cs:307-318
                if (e.def != null) p.value = Coerce(e.def, e.type);                          // VFXParameter.cs:354-368
                byName[e.name] = p;
            }
            foreach (VFXSlot slot in block.inputSlots)
            {
                string key = slot.name.StartsWith(VfxBlocks.CustomHLSL.parameterPrefix)      // "_" : CustomHLSL.cs:61,383-384
                    ? slot.name.Substring(VfxBlocks.CustomHLSL.parameterPrefix.Length) : slot.name;
                if (!byName.TryGetValue(key, out var p))
                {
                    Debug.LogWarning("VfxGraphBuilder: HLSL parameter '" + key + "' has no exposed parameter; it keeps its slot default.");
                    continue;
                }
                if (!p.outputSlots[0].Link(slot))                                            // VFXSlot.cs:799-819
                    throw new InvalidOperationException("Cannot link exposed '" + key + "' (" + p.type + ") to slot " + slot.name + " (" + slot.property.type + ")");
            }
            if (!string.IsNullOrEmpty(opts.mainTextureExposedName) && byName.TryGetValue(opts.mainTextureExposedName, out var texParam))
            {
                var s = output.inputSlots.FirstOrDefault(x => x.name == texSlot);
                if (s == null || !texParam.outputSlots[0].Link(s))
                    throw new InvalidOperationException("Cannot link exposed texture to output slot " + texSlot);
            }
            float y = 0;
            foreach (var p in byName.Values)                                                 // graph-window nicety only
            {
                try { p.CreateDefaultNode(new Vector2(-500, y)); } catch (Exception ex) { Debug.LogWarning(ex.Message); } // VFXParameter.cs:706-727
                y += 60;
            }

            // ---- 9. asset-level settings --------------------------------------------------------------------------------
            resource.cullingFlags = VFXCullingFlags.CullNone;                                // usage: VFXDataParticle.cs:295 ; mapping VFXAssetEditor.cs:441-452
            resource.instancingMode = VFXInstancingMode.Disabled;                            // usage: VFXGraph.cs:927
            var so = new SerializedObject(resource);                                         // property paths: VFXAssetEditor.cs:162-170
            so.FindProperty("m_Infos.m_UpdateMode").intValue =
                opts.deltaTimeUpdate ? (int)VFXUpdateMode.DeltaTime : 0;                     // VFXAssetEditor.cs:669-687 (0 = fixed delta time)
            so.FindProperty("m_Infos.m_PreWarmStepCount").intValue = 0;
            so.FindProperty("m_Infos.m_InitialEventName").stringValue = "OnPlay";            // template default (VFX/Editor/Templates/Empty.vfx)
            so.ApplyModifiedPropertiesWithoutUndo();

            // ---- 10. save + compile (compile happens in the importer: VFXGraph.cs:157-206) ------------------------------
            EditorUtility.SetDirty(graph);
            resource.WriteAssetWithSubAssets();                                              // VFXGraph.cs:429-434 (same as window Save: VFXView.cs:213-216)
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.SaveAssets();                                                      // persists ProjectSettings/VFXManager.asset changes

            asset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(assetPath);
            Validate(asset, assetPath, exposed);
            return asset;
        }

        // ------------------------------------------------------------------------------------------------------------------
        static VFXContext CreateHdrpContext(string fullTypeName)
        {
            // HDRP editor classes are internal and the HDRP Editor assembly does not grant us friend access
            // (HDRP/Editor/AssemblyInfo.cs:3-8), so instantiate by name and use the VFXContext base API.
            Type t = Type.GetType(fullTypeName + ", Unity.RenderPipelines.HighDefinition.Editor", true);
            return (VFXContext)ScriptableObject.CreateInstance(t);
        }

        static void SetEnum(VFXModel model, string setting, string valueName)
        {
            VFXSetting s = model.GetSetting(setting);                                        // VFXModel.cs:425-428 ; struct VFXSettingAttribute.cs:48-78
            if (!s.valid) throw new ArgumentException("Setting '" + setting + "' not found on " + model.GetType().Name);
            model.SetSettingValue(setting, Enum.Parse(s.field.FieldType, valueName));        // VFXModel.cs:287,311-336 (boxed value must have the field's enum type)
        }

        static void SetSlot(VFXModel model, string slotName, object value, bool required)
        {
            // VFXContext and VFXBlock both derive from VFXSlotContainerModel<,> which implements IVFXSlotContainer (VFXSlotContainerModel.cs:12-16,51).
            var container = model as IVFXSlotContainer;
            VFXSlot slot = container != null ? container.inputSlots.FirstOrDefault(s => s.name == slotName) : null;
            if (slot == null)
            {
                string msg = "Input slot '" + slotName + "' not found on " + model.GetType().Name;
                if (required) throw new ArgumentException(msg);
                Debug.LogWarning("VfxGraphBuilder: " + msg);
                return;
            }
            slot.value = value;                                                              // VFXSlot.cs:25-90
        }

        static object Coerce(object v, Type t)
        {
            if (v == null || t.IsInstanceOfType(v)) return v;
            if (t == typeof(float)) return Convert.ToSingle(v);
            if (t == typeof(int)) return Convert.ToInt32(v);
            if (t == typeof(uint)) return Convert.ToUInt32(v);
            if (t == typeof(bool)) return Convert.ToBoolean(v);
            if (t == typeof(Vector4) && v is Color c) return (Vector4)c;
            if (t == typeof(Vector4) && v is Vector3 v3) return new Vector4(v3.x, v3.y, v3.z, 0f);
            throw new ArgumentException("Default value of type " + v.GetType() + " cannot be used for exposed type " + t);
        }

        static void Validate(VisualEffectAsset asset, string assetPath, (string name, Type type, object def)[] exposed)
        {
            if (asset == null) throw new InvalidOperationException("VFX asset missing after import: " + assetPath);
            UnityEngine.Object[] subs = AssetDatabase.LoadAllAssetsAtPath(assetPath);        // shaders are added with ctx.AddObjectToAsset (VFXGraph.cs:1508-1522,1573)
            var computes = subs.OfType<ComputeShader>().ToList();
            var shaders = subs.OfType<Shader>().ToList();
            if (computes.Count < 1 || shaders.Count < 1)
                throw new InvalidOperationException("VFX compile produced " + computes.Count + " compute shader(s) and " + shaders.Count +
                    " shader(s) for " + assetPath + ". Check: HDRP asset active? log lines 'Unity cannot compile the VisualEffectAsset' (VFXGraphCompiledData.cs:1105)?");
            foreach (var cs in computes)
                foreach (var m in ShaderUtil.GetComputeShaderMessages(cs))                   // UNVERIFIED signature (name exists in UnityEditor.CoreModule.dll)
                    if (m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) // UNVERIFIED type/namespace
                        throw new InvalidOperationException("Compute shader error in " + cs.name + ": " + m.message);
            foreach (var sh in shaders)
                if (ShaderUtil.ShaderHasError(sh))                                           // UNVERIFIED signature (name exists in UnityEditor.CoreModule.dll)
                    throw new InvalidOperationException("Shader error in " + sh.name);
            var props = new List<VFXExposedProperty>();
            asset.GetExposedProperties(props);                                               // public runtime API (name exists in UnityEngine.VFXModule.dll)
            foreach (var e in exposed)
                if (!props.Any(p => p.name == e.name))
                    Debug.LogWarning("VfxGraphBuilder: exposed property '" + e.name + "' not reported by the compiled asset " + assetPath);
            Debug.Log("VfxGraphBuilder: OK " + assetPath + " computes=" + computes.Count + " shaders=" + shaders.Count + " exposed=" + props.Count);
        }
    }
}
