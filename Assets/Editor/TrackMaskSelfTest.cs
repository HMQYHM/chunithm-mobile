using System;
using System.Reflection;
using ChunithmMobile.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace ChunithmMobile.Editor
{
    // Covers the skin alpha leak and intentionally transparent saved settings.
    // The pixel check verifies alpha blending over a simulated note/track layer.
    public static class TrackMaskSelfTest
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        public static void Run()
        {
            const string key = "chunithm_mobile_settings_v1";
            bool had = PlayerPrefs.HasKey(key); string saved = PlayerPrefs.GetString(key);
            var root = new GameObject("Mask regression");
            try {
                PlayerPrefs.SetString(key,"{\"SettingsVersion\":26,\"TrackMaskEnabled\":true,\"TrackMaskOpacity\":0,\"FrameRateLimit\":60,\"VerticalSync\":true}");
                var settings = AppSettings.Load();
                Require(settings.TrackMaskOpacity==0,"explicit zero reset during load");
                settings.Save();
                Require(AppSettings.Load().TrackMaskOpacity==0,"explicit zero did not persist");
                string json=PlayerPrefs.GetString(key);
                Require(!json.Contains("FrameRateLimit") && !json.Contains("VerticalSync"),"obsolete settings survived save");
                PlayerPrefs.SetString(key,"{\"SettingsVersion\":26}");
                Require(AppSettings.Load().TrackMaskOpacity==90,"missing current opacity did not default to 90");
                PlayerPrefs.SetString(key,"{\"SettingsVersion\":4}");
                Require(AppSettings.Load().TrackMaskOpacity==90,"old settings lost default mask alpha");
                var canvasObject=new GameObject("Canvas",typeof(Canvas)); canvasObject.transform.SetParent(root.transform,false);
                var canvas=canvasObject.GetComponent<Canvas>();
                canvas.renderMode=RenderMode.ScreenSpaceOverlay;
                var shell=root.AddComponent<AppUiShell>();
                Set(shell,"canvas",canvas);
                var font=Resources.Load<Font>("Fonts/HarmonyOS_Sans_SC_Regular"); Set(shell,"font",font);Set(shell,"titleFont",font);
                Set(shell,"settings",new AppSettings {TrackMaskEnabled=true,TrackMaskOpacity=90,TrackMaskHeight=30});
                GameplaySkinManager.Initialize(GameplaySkinManager.BuiltinId);
                Call(shell,"BuildGameplay");
                var mask=(TrackMaskOverlay)typeof(AppUiShell).GetField("gameplayTrackMask",Private).GetValue(shell);
                var group=mask.GetComponent<CanvasGroup>();
                if(group==null)group=mask.gameObject.AddComponent<CanvasGroup>();
                group.alpha=0;
                mask.color=Color.clear;mask.canvasRenderer.SetAlpha(0);mask.enabled=false;
                var style=new GameplaySkinStyle();
                style.LayoutElements.Add(new GameplaySkinElementDefinition {Id="trackMask",Visible=false,Opacity=0,Color="#00000000"});
                Call(shell,"ApplyGameplaySkinLayout",style);
                mask.Configure(true,30,90);
                VerifyMesh(mask,.9f);
                Require(mask.isActiveAndEnabled && group.alpha==1 && !mask.raycastTarget && mask.canvasRenderer.GetAlpha()==1,
                    "skin left mask inactive/transparent");
                Require(mask.material==mask.defaultMaterial && mask.color==Color.white,"mask did not restore default alpha blend");
                Require(Mathf.Abs(mask.rectTransform.anchorMin.y-.7f)<.0001f && mask.rectTransform.anchorMax==Vector2.one,
                    "mask height does not cover selected upper track region");
                var pause=(Button)typeof(AppUiShell).GetField("gameplayPauseButton",Private).GetValue(shell);
                Require(mask.transform.parent==pause.transform.parent && mask.transform.GetSiblingIndex()<pause.transform.GetSiblingIndex(),
                    "mask covers foreground controls");
                Call(shell,"ApplyGameplaySkinLayout",new GameplaySkinStyle());mask.Configure(true,45,100);VerifyMesh(mask,1);
                mask.Configure(true,16,0);VerifyMesh(mask,0);
                mask.Configure(false,16,90);Require(!mask.gameObject.activeSelf,"disabled mask remains visible");
                CheckPixels(root);
                Debug.Log("Track mask regression passed: zero persistence, stale field cleanup, skin/default alpha, full quad, upper-track height, HUD ordering and rendered blend.");
            } finally {
                if(had)PlayerPrefs.SetString(key,saved);else PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();UnityEngine.Object.DestroyImmediate(root);
            }
        }
        private static void VerifyMesh(TrackMaskOverlay mask,float alpha)
        {
            using(var vertices=new VertexHelper()) {
                typeof(TrackMaskOverlay).GetMethod("OnPopulateMesh",Private,null,new[]{typeof(VertexHelper)},null).Invoke(mask,new object[]{vertices});
                Require(vertices.currentVertCount==4 && vertices.currentIndexCount==6,"mask not a full quad");
                UIVertex v=default;for(int i=0;i<4;i++){vertices.PopulateUIVertex(ref v,i);Require(Mathf.Abs(v.color.a/255f-alpha)<.005f,"wrong mask alpha");}
            }
        }
        private static void CheckPixels(GameObject root)
        {
            var cameraObject=new GameObject("Mask camera",typeof(Camera));cameraObject.transform.SetParent(root.transform,false);
            var camera=cameraObject.GetComponent<Camera>(); camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.white;
            camera.orthographic=true;camera.nearClipPlane=.01f;camera.farClipPlane=10;
            var target=new RenderTexture(128,128,16);camera.targetTexture=target;
            var canvasObject=new GameObject("Pixel canvas",typeof(Canvas));canvasObject.transform.SetParent(root.transform,false);
            var canvas=canvasObject.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            var maskObject=new GameObject("Pixel mask",typeof(RectTransform));maskObject.transform.SetParent(canvasObject.transform,false);
            var mask=maskObject.AddComponent<TrackMaskOverlay>();var pixels=new Texture2D(128,128,TextureFormat.RGB24,false);
            var previous=RenderTexture.active;
            try {
                foreach(int opacity in new[]{0,50,100}) {
                    mask.Configure(true,45,opacity);Canvas.ForceUpdateCanvases();camera.Render();
                    RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,128,128),0,0);pixels.Apply();
                    float top=pixels.GetPixel(64,110).grayscale,bottom=pixels.GetPixel(64,10).grayscale;
                    Require(bottom>.95f,"mask covers lower track");
                    if(opacity==0)Require(top>.95f,"zero is not transparent");
                    if(opacity==50)Require(top>.2f && top<.85f,"UI alpha blend failed");
                    if(opacity==100)Require(top<.05f,"opaque mask did not cover track");
                }
            } finally {RenderTexture.active=previous;camera.targetTexture=null;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(pixels);}
        }
        private static void Call(object target,string name,params object[] args)=>typeof(AppUiShell).GetMethod(name,Private).Invoke(target,args);
        private static void Set(object target,string name,object value)=>typeof(AppUiShell).GetField(name,Private).SetValue(target,value);
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
