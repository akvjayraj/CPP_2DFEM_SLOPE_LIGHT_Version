#ifndef MESH2D_C_API_H
#define MESH2D_C_API_H

#ifdef _WIN32
#ifdef MESH2D_NATIVE_EXPORTS
#define MESH2D_C_API __declspec(dllexport)
#else
#define MESH2D_C_API __declspec(dllimport)
#endif
#else
#define MESH2D_C_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

MESH2D_C_API void mesh2d_free_doubles(double* p);
MESH2D_C_API void mesh2d_free_ints(int* p);

/// 1-based node ids per triangle, length 3 * (*out_ne).
MESH2D_C_API int mesh2d_geom_rect_build(const double* x, int nx, const double* y, int ny, char dir,
                                        double** out_xy, int* out_nn, int** out_tris, int* out_ne);

MESH2D_C_API void mesh2d_geom_rect_triangle_ids(int iel, int nxe, int nye_physical, char dir, int* n1, int* n2,
                                                int* n3);

/// 0-based vertex indices, length 3 * (*out_nt).
MESH2D_C_API int mesh2d_polygon_ear(const double* xy, int npt, int** out_tris, int* out_nt);

MESH2D_C_API double mesh2d_polygon_area(const double* xy, int npt);

/// Circles: n_circles * 3 doubles (cx, cy, r). Poly holes: poly_hole_offsets length n_poly_holes+1
/// (offsets into poly_holes_xy in doubles, pairs x,y per vertex).
MESH2D_C_API int mesh2d_tunnel_generate(const double* outer_xy, int n_outer, const double* circles, int n_circles,
                                        const int* poly_hole_offsets, int n_poly_holes,
                                        const double* poly_holes_xy, double far_h, double near_h, int rings,
                                        double rgrowth, double first_thick, int max_nodes, double** out_xy, int* nn,
                                        int** out_active_tris, int* n_active, int** out_tunnel_tris, int* n_tunnel);

/// T3: tris length 3*nt3 (0-based node indices). Outputs new node list and T6 connectivity (6*nt3 ints).
MESH2D_C_API int mesh2d_t6_upgrade(const double* xy, int nn, const int* t3, int nt3, double** out_xy, int* nn_out,
                                    int** out_t6, int* nt6);

/// Binary blob layout — see mesh2d_c_api.cpp / Mesh2dNative.cs. Returns allocated output buffer; free with mesh2d_free_bytes.
MESH2D_C_API int mesh2d_graded_conforming(const unsigned char* blob, int blob_len, unsigned char** out_blob,
                                           int* out_len);
MESH2D_C_API void mesh2d_free_bytes(unsigned char* p);

#ifdef __cplusplus
}
#endif

#endif
