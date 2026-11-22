// stl2step: turns a printable STL part into a STEP solid for CAD programs.
//
// OpenSCAD can't export STEP, so case/make_step.sh exports each part as STL and runs this on it.
// It stitches the triangles into one closed solid and merges triangles that lie in the same plane
// into single flat faces, so the walls, pockets and holes' flat ends come out as clean faces.
// Round things (holes, rounded corners) stay as the many small flat facets OpenSCAD drew them with.
//
// Build (needs the OpenCascade dev packages, e.g. libocct-data-exchange-dev on Debian/Ubuntu):
//   see case/make_step.sh
// Use:
//   stl2step part.stl part.step

#include <BRepBuilderAPI_MakeSolid.hxx>
#include <BRepBuilderAPI_Sewing.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <BRepGProp.hxx>
#include <GProp_GProps.hxx>
#include <IFSelect_ReturnStatus.hxx>
#include <Interface_Static.hxx>
#include <STEPControl_Writer.hxx>
#include <ShapeFix_Solid.hxx>
#include <ShapeUpgrade_UnifySameDomain.hxx>
#include <StlAPI_Reader.hxx>
#include <TopExp_Explorer.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Shell.hxx>

#include <cstdio>

int main(int argc, char** argv) {
  if (argc != 3) {
    std::fprintf(stderr, "usage: stl2step part.stl part.step\n");
    return 2;
  }

  TopoDS_Shape triangles;
  if (!StlAPI_Reader().Read(triangles, argv[1])) {
    std::fprintf(stderr, "could not read %s\n", argv[1]);
    return 1;
  }

  // Stitch the loose triangles into closed shells (OpenSCAD's mesh is watertight; 1 µm tolerance).
  BRepBuilderAPI_Sewing sewing(1e-3);
  sewing.Add(triangles);
  sewing.Perform();

  // One solid from all the shells: a part with a hollow inside has more than one.
  BRepBuilderAPI_MakeSolid makeSolid;
  int shells = 0;
  for (TopExp_Explorer it(sewing.SewedShape(), TopAbs_SHELL); it.More(); it.Next(), ++shells)
    makeSolid.Add(TopoDS::Shell(it.Current()));
  if (shells == 0) {
    std::fprintf(stderr, "%s: the triangles don't form a closed shell\n", argv[1]);
    return 1;
  }
  ShapeFix_Solid fixSolid(makeSolid.Solid());  // puts the outer and inner shells the right way round
  fixSolid.Perform();

  // Merge triangles lying in one plane into a single face.
  ShapeUpgrade_UnifySameDomain unify(fixSolid.Solid(), true, true, false);
  unify.Build();
  TopoDS_Shape solid = unify.Shape();

  if (!BRepCheck_Analyzer(solid).IsValid()) {
    std::fprintf(stderr, "%s: the solid isn't valid\n", argv[1]);
    return 1;
  }
  GProp_GProps props;
  BRepGProp::VolumeProperties(solid, props);
  int faces = 0;
  for (TopExp_Explorer it(solid, TopAbs_FACE); it.More(); it.Next()) ++faces;

  Interface_Static::SetCVal("write.step.schema", "AP214IS");
  Interface_Static::SetCVal("write.step.unit", "MM");
  STEPControl_Writer writer;
  if (writer.Transfer(solid, STEPControl_AsIs) != IFSelect_RetDone || writer.Write(argv[2]) != IFSelect_RetDone) {
    std::fprintf(stderr, "could not write %s\n", argv[2]);
    return 1;
  }
  std::printf("%s: %d faces, %.1f cm3\n", argv[2], faces, props.Mass() / 1000.0);
  return 0;
}
