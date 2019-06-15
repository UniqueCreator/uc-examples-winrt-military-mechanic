#include "default_signature.hlsli"

Texture2D<float4>     albedo          : register( t1 );
RWTexture2D<float4>   lightingBuffer  : register( u1 );

[numthreads(8, 8, 1)]
[RootSignature( MyRS2 ) ]
void main( uint3 DTid    : SV_DispatchThreadID, uint3 gtid : SV_GroupThreadID )
{
    lightingBuffer[DTid.xy] = albedo[DTid.xy];
}

