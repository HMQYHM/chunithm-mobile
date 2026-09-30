// Render output is premultiplied; convert it before the UI's straight-alpha blend.
Shader "CHUNITHM/SkinLive2D/Composite"
{
    Properties { _MainTex ("Model output", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #include "UnityCG.cginc"
            #pragma vertex vert
            #pragma fragment frag
            sampler2D _MainTex;
            struct Input { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            struct Output { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
            Output vert(Input i) { Output o; o.vertex=UnityObjectToClipPos(i.vertex); o.uv=i.uv; o.color=i.color; return o; }
            fixed4 frag(Output i):SV_Target
            {
                fixed4 color = tex2D(_MainTex,i.uv);
                color.rgb /= max(color.a, .001);
                return color * i.color;
            }
            ENDCG
        }
    }
}
