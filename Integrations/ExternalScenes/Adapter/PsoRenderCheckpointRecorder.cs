using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Yanagisawa.ShaderHitchPipeline;

// Correctness-only readback of the actual original route. No camera pose or
// Timeline is changed. Timing runs must not enable this component.
public sealed class PsoRenderCheckpointRecorder : MonoBehaviour
{
    [Serializable] public sealed class Checkpoint
    {
        public string key, scene, status, camera, timelineAsset, file, sha256, error;
        public int requestedFrame, capturedFrame, width, height;
        public double targetTime, actualTime, timelineDuration, maximumLatenessSeconds = 0.25;
        public Vector3 position; public Quaternion rotation;
        public bool written, withinTimeTolerance;
    }
    [Serializable] public sealed class Receipt
    {
        public int version = 1;
        public string contract = "original-route-checkpoints-v1";
        public string[] expectedKeys;
        public bool correctnessOnly = true, normalQuit;
        public string visualAcceptance = "pending independent image review; capture alone is not acceptance";
        public Checkpoint[] checkpoints;
    }
    readonly List<Checkpoint> checkpoints = new List<Checkpoint>();
    readonly HashSet<string> requested = new HashSet<string>();
    string output; Receipt receipt;
    public void Configure(string[] scenes)
    {
        if (Application.isBatchMode) throw new InvalidOperationException("Fixed frame readback requires a graphics Player with end-of-frame callbacks, without -batchmode.");
        output = PsoCommandLine.Current.GetString(PsoConstants.OutputArgument, "");
        if (string.IsNullOrWhiteSpace(output) || File.Exists(Path.Combine(output, "render-checkpoints.json")))
            throw new InvalidDataException("Fresh checkpoint output required.");
        receipt = new Receipt { expectedKeys = scenes.SelectMany(s => new[] { s + "-warmup-1s", s + "-running-10", s + "-running-50", s + "-running-90" }).ToArray() };
    }
    public void Observe(string scene, string status, string camera, string asset, double time,
                        double duration, Vector3 position, Quaternion rotation)
    {
        if (receipt == null) return;
        if (status == "Warming") Request("warmup-1s", 1);
        if (status == "Running") { Request("running-10", duration * .1); Request("running-50", duration * .5); Request("running-90", duration * .9); }
        void Request(string label, double target)
        {
            string key = scene + "-" + label;
            if (time < target || !receipt.expectedKeys.Contains(key) || !requested.Add(key)) return;
            var checkpoint = new Checkpoint { key = key, scene = scene, status = status, camera = camera,
                timelineAsset = asset, targetTime = target, actualTime = time, timelineDuration = duration,
                requestedFrame = Time.frameCount, position = position, rotation = rotation,
                withinTimeTolerance = time - target <= .25, file = key + ".png" };
            checkpoints.Add(checkpoint);
            StartCoroutine(Capture(checkpoint));
        }
    }
    IEnumerator Capture(Checkpoint checkpoint)
    {
        yield return new WaitForEndOfFrame();
        Texture2D texture = null;
        try
        {
            checkpoint.capturedFrame = Time.frameCount;
            if (checkpoint.capturedFrame != checkpoint.requestedFrame)
                throw new InvalidDataException("Checkpoint readback crossed Unity frames.");
            texture = ScreenCapture.CaptureScreenshotAsTexture();
            checkpoint.width = texture.width; checkpoint.height = texture.height;
            string path = Path.Combine(output, checkpoint.file);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            {
                byte[] png = texture.EncodeToPNG(); stream.Write(png, 0, png.Length);
            }
            checkpoint.sha256 = PsoFileUtility.ComputeSha256(path); checkpoint.written = true;
        }
        catch (Exception error) { checkpoint.error = error.GetType().Name + ": " + error.Message; Debug.LogException(error); }
        finally { if (texture != null) Destroy(texture); }
    }
    void OnApplicationQuit()
    {
        if (receipt == null) return;
        receipt.normalQuit = true; receipt.checkpoints = checkpoints.ToArray();
        File.WriteAllText(Path.Combine(output, "render-checkpoints.json"), JsonUtility.ToJson(receipt, true));
    }
}
