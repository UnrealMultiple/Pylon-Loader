using System.Runtime.InteropServices;
unsafe {
 var p = Marshal.AllocHGlobal(1024);
 try {
  new Span<byte>((void*)p,1024).Clear();
  var proj = new FixtureProjectile { ai = new Float_FixedArray_3View { _p=p }, localAI = new Float_FixedArray_3View { _p=p+16 }, oldPos = new Vector2_DynamicArray_120View { _p=p+32 } };
  proj.ai[1] = 0f; proj.ai[1] += 2f; proj.ai[1] *= 3f;
  proj.localAI[1] += 1f;
  if (proj.ai[1] != 6 || proj.localAI[1] != 1 || proj.ai.Length != 3) throw new Exception("float write/compound");
  *(int*)(p+32)=2;
  proj.oldPos[1].X = 42;
  if (*(float*)(p+32+4+8) != 42 || proj.oldPos.Length != 2) throw new Exception("nested struct ref");
  var copy=proj.oldPos; copy[0].X=13;
  if(proj.oldPos[0].X != 13) throw new Exception("view copy");
  ExpectBounds(()=> { _=proj.ai[-1]; }); ExpectBounds(()=> { _=proj.ai[3]; });
  ExpectBounds(()=> { _=proj.oldPos[2]; }); ExpectBounds(()=> { _=proj.oldPos[int.MaxValue]; });
  *(int*)(p+32) = -1; ExpectBounds(()=> { _=proj.oldPos.Length; });
  *(int*)(p+32) = 121; ExpectBounds(()=> { _=proj.oldPos.Length; });
  Console.WriteLine("PASS generated IL execution: fixed float read/write/compound, dynamic length, nested struct field write, copied view, negative/out-of-range/corrupt length");
 } finally { Marshal.FreeHGlobal(p); }
}
static void ExpectBounds(Action action) { try { action(); } catch(IndexOutOfRangeException) { return; } throw new Exception("Missing bounds check"); }
class FixtureProjectile {
 public Float_FixedArray_3View ai {get;set;}
 public Float_FixedArray_3View localAI {get;set;}
 public Vector2_DynamicArray_120View oldPos {get;set;}
}
