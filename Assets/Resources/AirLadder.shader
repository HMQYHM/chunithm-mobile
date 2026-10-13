Shader "ChuMobile/AirLadder" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend", Float) = 10
    }
    SubShader {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off ZWrite Off
        Blend SrcAlpha [_DstBlend]
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 position:SV_POSITION; float2 uv:TEXCOORD0; };
            sampler2D _MainTex; float4 _Color;
            v2f vert(appdata v) { v2f o; o.position=UnityObjectToClipPos(v.vertex); o.uv=v.uv; return o; }
            fixed4 frag(v2f i):SV_Target { return tex2D(_MainTex,i.uv)*_Color; }
            ENDCG
        }
    }
}
