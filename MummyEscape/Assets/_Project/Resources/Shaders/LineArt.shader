// Strokes of LineArt drawn as smooth shapes: each segment is a quad carrying, in TEXCOORD0, the pixel's place along and
// across the segment (x, y), its length (z) and half its width (w). The distance to the segment gives a capsule with
// round ends and anti-aliased edges, so the joints of a polyline close up seamlessly. w < 0: a soft glow fading out
// over -w. w == 0: a filled shape, drawn as is. The output is premultiplied: [_Op] = Max blends glows without doubling.
Shader "Hidden/MummyRush/LineArt"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Op ("Blend op", Float) = 0
        _Dst ("Dst blend", Float) = 10
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        BlendOp [_Op]
        Blend One [_Dst]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float4 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float4 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float alpha = 1;
                if (i.uv.w != 0)
                {
                    float along = i.uv.x;
                    float dx = along < 0 ? along : max(0, along - i.uv.z);
                    float d = length(float2(dx, i.uv.y));
                    if (i.uv.w > 0)
                        alpha = saturate((i.uv.w - d) / max(fwidth(d), 1e-4) + 0.5);
                    else
                    {
                        float k = saturate(1 - d / -i.uv.w);
                        alpha = k * k;
                    }
                }
                float a = i.color.a * alpha;
                return fixed4(i.color.rgb * a, a);
            }
            ENDCG
        }
    }
}
