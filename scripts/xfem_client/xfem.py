"""JSON-RPC client for the live GeotechSlopeApp GUI (named pipe)."""

from __future__ import annotations

import json
import os
import time


class _Rpc:
    def __init__(self, pipe_name: str | None = None):
        name = pipe_name or os.environ.get("XFEM_RPC_PIPE")
        if not name:
            raise RuntimeError("XFEM_RPC_PIPE is not set. Run this script from Script Editor → Execute.")
        path = rf"\\.\pipe\{name}"
        self._fh = None
        last = None
        for _ in range(50):
            try:
                self._fh = open(path, "r+b", buffering=0)
                break
            except OSError as ex:
                last = ex
                time.sleep(0.1)
        if self._fh is None:
            raise RuntimeError(f"Could not connect to GUI pipe {name}: {last}")
        self._n = 0

    def call(self, method: str, **params):
        self._n += 1
        req = {"id": self._n, "method": method, "params": params}
        self._fh.write((json.dumps(req) + "\n").encode("utf-8"))
        line = self._fh.readline()
        if not line:
            raise RuntimeError("GUI closed the RPC pipe.")
        resp = json.loads(line.decode("utf-8"))
        if not resp.get("ok", False):
            raise RuntimeError(resp.get("error") or "RPC error")
        return resp.get("result")


class _Geometry:
    def __init__(self, rpc: _Rpc):
        self._rpc = rpc

    def set_outline(self, points):
        pts = [[float(p[0]), float(p[1])] for p in points]
        return self._rpc.call("geometry.set_outline", points=pts)

    def add_layer(self, elevation: float):
        return self._rpc.call("geometry.add_layer", elevation=float(elevation))


class _Materials:
    def __init__(self, rpc: _Rpc):
        self._rpc = rpc

    def add(self, name, c_kpa, phi_deg, e_kpa, nu=0.3, gamma=20.0):
        return self._rpc.call(
            "materials.add",
            name=name,
            c_kpa=float(c_kpa),
            phi_deg=float(phi_deg),
            e_kpa=float(e_kpa),
            nu=float(nu),
            gamma=float(gamma),
        )


class _Mesh:
    def __init__(self, rpc: _Rpc):
        self._rpc = rpc

    def generate(self, element="Q4", size=None):
        kwargs = {"element": str(element)}
        if size is not None:
            kwargs["size"] = float(size)
        return self._rpc.call("mesh.generate", **kwargs)


class _Bcs:
    def __init__(self, rpc: _Rpc):
        self._rpc = rpc

    def apply_slope_defaults(self):
        """Left/right Roller X, base Roller Y, corners Fixed."""
        return self._rpc.call("bcs.apply_slope_defaults")

    def clear(self):
        return self._rpc.call("bcs.clear")


class _Fe:
    def __init__(self, rpc: _Rpc):
        self._rpc = rpc

    def set_ssr(self, on=True):
        """Turn Slope stability – strength reduction (SRF/SSR) ON or OFF."""
        return self._rpc.call("fe.set_ssr", on=bool(on))

    def ssr_on(self):
        return bool(self._rpc.call("fe.ssr_on"))

    def run(self):
        return self._rpc.call("fe.run")

    def srf(self):
        return self._rpc.call("fe.srf")


class Project:
    def __init__(self, pipe_name: str | None = None):
        self._rpc = _Rpc(pipe_name)
        self.geometry = _Geometry(self._rpc)
        self.materials = _Materials(self._rpc)
        self.mesh = _Mesh(self._rpc)
        self.bcs = _Bcs(self._rpc)
        self.fe = _Fe(self._rpc)

    def clear(self):
        return self._rpc.call("clear")

    def set_title(self, name: str):
        return self._rpc.call("set_title", name=name)

    def save(self, path: str):
        return self._rpc.call("project.save", path=path)

    def load(self, path: str):
        return self._rpc.call("project.load", path=path)


project = Project()
