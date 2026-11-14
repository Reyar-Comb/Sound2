#ifndef SOUND2_SCREEN_POINT_LATTICE
#define SOUND2_SCREEN_POINT_LATTICE
// Stable ordered selection shared by world and entry-screen dots. Never moves the lattice.
float OrderedRank(uint2 cell)
{
    uint rank=0;
    [unroll] for(uint bit=0;bit<8;bit++)
    {
        uint x=(cell.x>>bit)&1,y=(cell.y>>bit)&1;
        rank|=(((x^y)<<1)|y)<<(2*(7-bit));
    }
    return (rank+.5)/65536.0;
}
#endif
