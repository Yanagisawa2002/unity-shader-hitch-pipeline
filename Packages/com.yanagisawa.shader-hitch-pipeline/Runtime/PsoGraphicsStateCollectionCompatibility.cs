using System;
#if !UNITY_6000_5_OR_NEWER
using System.Collections.Generic;
#endif
#if UNITY_6000_5_OR_NEWER
using GraphicsStateCollection = UnityEngine.Rendering.GraphicsStateCollection;
#else
using GraphicsStateCollection = UnityEngine.Experimental.Rendering.GraphicsStateCollection;
#endif

namespace Yanagisawa.ShaderHitchPipeline
{
    /// <summary>
    /// Contains the narrow compatibility seam for Unity's experimental graphics-state
    /// API. Unity 6000.5 moved the type to UnityEngine.Rendering and added Append;
    /// earlier Unity 6 releases expose the same data under Experimental.Rendering.
    /// </summary>
    public static class PsoGraphicsStateCollectionCompatibility
    {
        public static bool Append(
            GraphicsStateCollection destination,
            GraphicsStateCollection source)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (source == null)
                throw new ArgumentNullException(nameof(source));

#if UNITY_6000_5_OR_NEWER
            return destination.Append(source);
#else
            try
            {
                var variants =
                    new List<GraphicsStateCollection.ShaderVariant>();
                source.GetVariants(variants);
                for (int variantIndex = 0; variantIndex < variants.Count; variantIndex++)
                {
                    GraphicsStateCollection.ShaderVariant variant = variants[variantIndex];
                    destination.AddVariant(
                        variant.shader,
                        variant.passId,
                        variant.keywords);

                    var states =
                        new List<GraphicsStateCollection.GraphicsState>();
                    source.GetGraphicsStatesForVariant(variant, states);
                    for (int stateIndex = 0; stateIndex < states.Count; stateIndex++)
                    {
                        destination.AddGraphicsStateForVariant(
                            variant.shader,
                            variant.passId,
                            variant.keywords,
                            states[stateIndex]);
                    }
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
#endif
        }
    }
}
