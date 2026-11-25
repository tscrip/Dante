#!/usr/bin/env bash
# Exports the yellow-board case parts as STL and STEP (for CAD programs) from cyd35_tilt_case.scad.
# Needs openscad, g++ and the OpenCascade dev packages, e.g. on Debian/Ubuntu:
#   sudo apt-get install openscad g++ libocct-data-exchange-dev libocct-modeling-algorithms-dev
# Run from anywhere: case/make_step.sh
set -euo pipefail
cd "$(dirname "$0")"

tool="$(mktemp -d)/stl2step"
occt=/usr/include/opencascade
# OpenCascade 7.8 and later bundle the STEP code as TKDESTEP; older versions split it up (the second try).
g++ -O2 -w -std=c++17 -I"$occt" tools/stl2step.cpp -o "$tool" \
  -lTKDESTEP -lTKXSBase -lTKSTL -lTKShHealing -lTKTopAlgo -lTKBRep -lTKGeomAlgo -lTKGeomBase \
  -lTKG3d -lTKG2d -lTKMath -lTKernel -lTKBO -lTKPrim 2>/dev/null ||
g++ -O2 -w -std=c++17 -I"$occt" tools/stl2step.cpp -o "$tool" \
  -lTKSTEP -lTKSTEPAttr -lTKSTEP209 -lTKSTEPBase -lTKXSBase -lTKSTL -lTKShHealing -lTKTopAlgo \
  -lTKBRep -lTKGeomAlgo -lTKGeomBase -lTKG3d -lTKG2d -lTKMath -lTKernel -lTKBO -lTKPrim

for part in front back stand; do
  openscad --export-format binstl -D "part=\"$part\"" -o "cyd35_$part.stl" cyd35_tilt_case.scad 2>/dev/null
  "$tool" "cyd35_$part.stl" "cyd35_$part.step" | grep -E "faces|valid|could"  # hide the library's own report
done
