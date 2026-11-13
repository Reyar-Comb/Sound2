# Clipper2

Source: https://github.com/AngusJohnson/Clipper2/tree/Clipper2_2.0.1/CSharp/Clipper2Lib

Pinned release: Clipper2_2.0.1. Used only in the Unity Editor for polygon unions, offsets, hole subtraction, and triangulation. Distributed under the Boost Software License 1.0; see LICENSE.txt. Additional notices are retained in HashCode.cs.

Local integration change: `#nullable enable annotations` added to Clipper.Offset.cs, Clipper.Triangulation.cs, HashCode.cs and PooledList.cs, matching the nullable annotations already enabled in the other upstream files. No algorithm changes.
