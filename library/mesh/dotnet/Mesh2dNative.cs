using System;
using System.Runtime.InteropServices;

namespace GeotechSlopeApp.Backend;

/// <summary>P/Invoke to native mesh2d_native.dll (C++ implementation in library/mesh).</summary>
internal static class Mesh2dNative
{
    private const string Dll = "mesh2d_native";

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void mesh2d_free_doubles(IntPtr p);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void mesh2d_free_ints(IntPtr p);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void mesh2d_free_bytes(IntPtr p);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void mesh2d_geom_rect_triangle_ids(int iel, int nxe, int nye_physical, char dir,
        out int n1, out int n2, out int n3);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mesh2d_geom_rect_build(
        [In] double[] x, int nx,
        [In] double[] y, int ny,
        char dir,
        out IntPtr out_xy, out int out_nn,
        out IntPtr out_tris, out int out_ne);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mesh2d_polygon_ear(
        [In] double[] xy, int npt,
        out IntPtr out_tris, out int out_nt);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern double mesh2d_polygon_area([In] double[] xy, int npt);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mesh2d_tunnel_generate(
        [In] double[] outer_xy, int n_outer,
        [In] double[]? circles, int n_circles,
        [In] int[]? poly_hole_offsets, int n_poly_holes,
        [In] double[]? poly_holes_xy,
        double far_h, double near_h, int rings, double rgrowth, double first_thick, int max_nodes,
        out IntPtr out_xy, out int nn,
        out IntPtr out_active_tris, out int n_active,
        out IntPtr out_tunnel_tris, out int n_tunnel);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mesh2d_t6_upgrade(
        [In] double[] xy, int nn,
        [In] int[] t3, int nt3,
        out IntPtr out_xy, out int nn_out,
        out IntPtr out_t6, out int nt6);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mesh2d_graded_conforming(
        [In] byte[] blob, int blob_len,
        out IntPtr out_blob, out int out_len);
}
