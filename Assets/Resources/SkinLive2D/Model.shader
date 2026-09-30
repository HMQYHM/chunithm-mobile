// Built-in pipeline rendering of SDK-evaluated Cubism geometry.
Shader "CHUNITHM/SkinLive2D/Model"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _SrcBlend ("Source blend", Int) = 1
        _DstBlend ("Destination blend", Int) = 10
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _ClipTex;
        float _UseMask, _InvertMask, _Opacity;
        float4 _Multiply, _Screen;
        struct Input { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
        struct Output { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float4 screen:TEXCOORD1; float4 color:COLOR; };
        Output vert(Input input)
        {
            Output o;
            o.vertex = UnityObjectToClipPos(input.vertex);
            o.screen = ComputeScreenPos(o.vertex);
            o.uv = input.uv;
            o.color = input.color;
            return o;
        }
        fixed4 frag(Output input):SV_Target
        {
            fixed4 color = tex2D(_MainTex, input.uv);
            color.rgb *= _Multiply.rgb;
            color.rgb = color.rgb + _Screen.rgb - color.rgb * _Screen.rgb;
            color *= input.color;
            float mask = tex2Dproj(_ClipTex, UNITY_PROJ_COORD(input.screen)).a;
            mask = lerp(mask, 1-mask, _InvertMask);
            color.a *= lerp(1, mask, _UseMask) * _Opacity;
            color.rgb *= color.a;
            return color;
        }
        fixed4 maskFrag(Output input):SV_Target
        {
            return fixed4(0,0,0,tex2D(_MainTex,input.uv).a);
        }
        ENDCG
        Pass
        {
            Blend [_SrcBlend] [_DstBlend], One OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
        Pass
        {
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment maskFrag
            ENDCG
        }
    }
}
