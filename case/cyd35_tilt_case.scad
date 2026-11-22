// DANTE case for the "yellow board" (ESP32-3248S035C, the 3.5" "CYD"), standing at a tilt.
//
// Three printed parts:
//   front  the shell: the board drops in face-down, the screen shows through the window
//   back   snaps into the shell; four posts press the board against the front
//   stand  holds the closed case at `tilt` degrees from the desk (60 by default)
//
// The board fit (pocket, window, corner pads, sensor holes, USB opening, back posts) is measured from
// the "CYD 3.5 case (ESP32-3248S035)" on Printables (model 739905), which fits the board exactly.
// All sizes in mm.
//
// Render one part at a time for printing:
//   openscad -D 'part="front"' -o case/cyd35_front.stl case/cyd35_tilt_case.scad
//   openscad -D 'part="back"'  -o case/cyd35_back.stl  case/cyd35_tilt_case.scad
//   openscad -D 'part="stand"' -o case/cyd35_stand.stl case/cyd35_tilt_case.scad
//
// Print orientation (no supports needed): front face down, back with its flat outside down,
// stand on its base, all exactly as the "print" layout and the STL files have them.

part = "assembled"; // [assembled, print, front, back, stand]

/* [Tilt] */
tilt         = 60;    // angle between the desk and the screen (90 = upright)
usb_on_right = true;  // seen from the front. false stands the case the other way up (USB on the left)

/* [Board pocket (measured; change only if your board differs)] */
pcb_w    = 102.3; // pocket width: the board's long side
pcb_h    = 55.5;  // pocket height: the board's short side
window_w = 86.3;  // screen window, centred; the window is the full pocket height
wall     = 1.8;   // side walls
front    = 1.5;   // front plate beside the screen
depth    = 14;    // shell depth, front face to back rim
pad_z    = 4.0;   // the board rests on corner pads this far behind the front face
hole_d   = 2.9;   // holes in the corner pads, under the board's mounting holes
corner_r = 1.0;   // outside corner rounding

/* [Back cover] */
lid_z     = 12.25; // inner face of the back cover
lid_t     = 1.5;   // back cover thickness (sits 0.25 below the rim)
lid_clr   = 0.15;  // gap around the back cover
snap      = 0.5;   // how far the snap tabs reach into the groove
post_z    = 6.75;  // the posts touch the back of the board at this depth
rib_z     = 10.25; // stiffening ribs reach down to here

/* [Stand] */
seat_h     = 3;    // material under the case's lowest corner
rest_len   = 32;   // how far up the back of the case the stand supports it
foot_depth = 60;   // desk footprint behind the case: longer = harder to tip over when you tap the top
tail_h     = 5;    // height of the foot behind the backrest
lip_h      = 2.5;  // lip in front of the screen's bottom edge (keep it below the picture)
lip_t      = 2.4;  // lip thickness
cheek_h    = 8;    // side cheeks hold the case sideways; kept low so the USB plug clears them
cheek_t    = 2.4;  // cheek thickness
fit        = 0.3;  // gap between the case and the stand's cheeks

$fn = 40;

c = cos(tilt);
s = sin(tilt);
eps = 0.01;

// ---------------------------------------------------------------------------------------------
// Front shell, in the board's own frame: X along the long side, Y along the short side,
// Z = 0 at the screen face, growing toward the back. The pocket is X 0..pcb_w, Y 0..pcb_h.
// ---------------------------------------------------------------------------------------------

win_x = (pcb_w - window_w) / 2;

// Pads the board's front rests on: [x0, x1, y0, y1]. The gaps between them leave room for parts
// on the front of the board.
pads = [
    [0, 8, 0, 16], [0, 8, 48, pcb_h],
    [pcb_w - 8, pcb_w, 0, 8.5], [pcb_w - 8, pcb_w, 48, pcb_h],
];
holes = [[3.5, 3.5], [pcb_w - 3.5, 3.5], [3.5, pcb_h - 3.5], [pcb_w - 3.5, pcb_h - 3.5]];

// The snap tabs on the back cover, and the pry notches above them on one long side: [x0, x1].
tabs = [[25, 37], [87.3, 99.3]];

module rounded_box(x0, y0, x1, y1, z0, z1, r) {
    translate([0, 0, z0]) linear_extrude(z1 - z0)
        offset(r = r) offset(delta = -r) translate([x0, y0]) square([x1 - x0, y1 - y0]);
}

// The groove the back cover's snap tabs click into, along both long walls: widest at lid_z,
// closing to nothing at the rim and a little below lid_z (a lead-in for printing).
module snap_groove() {
    g = snap + 0.25;
    rotate([90, 0, 90]) translate([0, 0, -eps]) linear_extrude(pcb_w + 2 * eps)
        polygon([[-g, lid_z], [0, lid_z - 0.6], [pcb_h, lid_z - 0.6], [pcb_h + g, lid_z],
                 [pcb_h, depth + eps], [0, depth + eps]]);
}

module front_shell() {
    difference() {
        union() {
            difference() {
                rounded_box(-wall, -wall, pcb_w + wall, pcb_h + wall, 0, depth, corner_r);
                translate([0, 0, front]) cube([pcb_w, pcb_h, depth]);          // board pocket
            }
            for (p = pads) translate([p[0], p[2], front - eps]) cube([p[1] - p[0], p[3] - p[2], pad_z - front + eps]);
        }
        translate([win_x, 0, -1]) cube([window_w, pcb_h, front + 2]);            // screen window
        for (h = holes) translate([h[0], h[1], front]) cylinder(d = hole_d, h = pad_z);
        translate([2, 10, -1]) cube([2.5, 4, pad_z + 1]);                        // light sensor slot
        translate([3.9, 44, -1]) cylinder(d = 2.9, h = front + 2);               // small window beside it
        // USB opening in the end wall (rounded slot)
        translate([pcb_w - 1, 38.25, 7]) rotate([0, 90, 0]) hull()
            for (dy = [-3.0, 3.0]) translate([0, dy, 0]) cylinder(d = 3.5, h = wall + 2);
        snap_groove();
        // pry notches on one long wall, above the snap tabs: lever the back out with a fingernail
        for (t = tabs) translate([t[0], pcb_h - 1, depth - 1.5]) cube([t[1] - t[0], wall + 2, 2]);
    }
}

// ---------------------------------------------------------------------------------------------
// Back cover, same frame. Snaps in from the back; posts press the board into the shell.
// ---------------------------------------------------------------------------------------------

lid_top = lid_z + lid_t;

posts = [ // [x0, x1, y0, y1]: where the original's posts touch the board between its parts
    [88.8, 92.8, 2.16, 6.16], [86.8, 90.8, 49.34, 53.34],
    [19, 27, 13.5, 16.5], [12, 20, 39, 42],
];
vents = [ // [x0, x1, y0, y1]: over the ESP32 and the power parts
    [7, 26, 18.5, 35], [77.8, 91.8, 21, 34], [16, 24, 3, 8.5],
];

module snap_tab(x0, x1, side) {
    // side -1: the y = 0 edge, +1: the y = pcb_h edge. Widest at lid_z, tapering both ways.
    y = side < 0 ? lid_clr : pcb_h - lid_clr;
    o = snap + lid_clr;
    translate([x0, 0, 0]) rotate([90, 0, 90]) linear_extrude(x1 - x0)
        polygon(side < 0
            ? [[y + 1, lid_z - 0.6], [y, lid_z - 0.6], [y - o, lid_z], [y, lid_top], [y + 1, lid_top]]
            : [[y - 1, lid_z - 0.6], [y, lid_z - 0.6], [y + o, lid_z], [y, lid_top], [y - 1, lid_top]]);
}

module back_cover() {
    difference() {
        union() {
            translate([lid_clr, lid_clr, lid_z]) cube([pcb_w - 2 * lid_clr, pcb_h - 2 * lid_clr, lid_t]);
            for (p = posts) translate([p[0], p[2], post_z]) cube([p[1] - p[0], p[3] - p[2], lid_z - post_z + eps]);
            // stiffening ribs
            for (x = [27, 50.4, 73.8]) translate([x, lid_clr + 0.5, rib_z]) cube([2, pcb_h - 2 * lid_clr - 1, lid_z - rib_z + eps]);
            for (y = [7.45, 26.35, 47.27]) translate([27, y, rib_z]) cube([48.8, 2, lid_z - rib_z + eps]);
            for (t = tabs, side = [-1, 1]) snap_tab(t[0], t[1], side);
        }
        // vents as slots, so the cover stays stiff
        for (v = vents) for (y = [v[2] + 1 : 3.5 : v[3] - 1.5])
            translate([v[0], y, lid_z - 1]) linear_extrude(lid_t + 2)
                hull() for (x = [v[0] + 1, v[1] - 1]) translate([x - v[0], 1]) circle(d = 2);
    }
}

// ---------------------------------------------------------------------------------------------
// Standing the case up. `orient` turns the board frame so the edge that goes on the desk is y = 0;
// `place` tilts it back and puts the case's lowest corner (its back-bottom edge) on the stand.
// World: X across, Y away from you (the screen faces -Y), Z up, desk at Z = 0.
// ---------------------------------------------------------------------------------------------

module orient() {
    if (usb_on_right) translate([pcb_w, pcb_h, 0]) rotate([0, 0, 180]) children();
    else children();
}

// up the face u = (0, c, s); into the case n = (0, s, -c); the board's X runs right to left.
module place() {
    multmatrix([[-1, 0, 0, pcb_w / 2],
                [0, c, s, wall * c - depth * s],
                [0, s, -c, seat_h + wall * s + depth * c],
                [0, 0, 0, 1]]) children();
}

// Side view of the stand, in (Y, Z) world coordinates. V is where the case's back-bottom edge sits.
function v2(a, b) = [a[0] + b[0], a[1] + b[1]];
function sc(v, k) = [v[0] * k, v[1] * k];
U  = [c, s];        // up the face
NI = [-s, c];       // out of the screen (forward and up)
V  = [0, seat_h];
F  = v2(V, sc(NI, depth + fit));           // just in front of the case's front-bottom edge
L1 = v2(F, sc(U, lip_h));
L3 = v2(F, sc(NI, lip_t));
L2 = v2(L3, sc(U, lip_h));
L4 = [L3[0], 0];
R  = v2(V, sc(U, rest_len));               // top of the backrest
T1 = v2(R, [3, 0]);
T2 = [T1[0] + (T1[1] - tail_h) / tan(50), tail_h];
foot_back = max(foot_depth, T2[0] + 5);
T3 = [foot_back, tail_h];
T4 = [foot_back, 0];

module stand_profile() {
    difference() {
        offset(r = 1) offset(delta = -1)
            polygon([L4, L3, L2, L1, F, V, R, T1, T2, T3, T4]);
        translate(V) circle(r = 0.8); // relief so the case's corner seats fully
    }
}

module cheek_profile() {
    h = wall + cheek_h;
    // the extra offsets merge the two outlines cleanly (no hairline slivers where their edges meet)
    offset(delta = -0.05) offset(delta = 0.05) union() {
        stand_profile();
        polygon([F, V, v2(V, sc(U, h)), v2(F, sc(U, h))]);
    }
}

module stand() {
    inner = pcb_w + 2 * wall + 2 * fit;
    outer = inner + 2 * cheek_t;
    rotate([90, 0, 90]) {
        translate([0, 0, -inner / 2]) linear_extrude(inner) stand_profile();
        for (z = [-outer / 2, inner / 2 - eps]) translate([0, 0, z]) linear_extrude(cheek_t + eps) cheek_profile();
    }
}

assert(T2[0] < foot_back, "the foot is too short for this tilt and backrest");

// ---------------------------------------------------------------------------------------------

if (part == "front") front_shell();
else if (part == "back") mirror([0, 0, 1]) translate([0, 0, -lid_top]) back_cover();
else if (part == "stand") stand();
else if (part == "print") {
    front_shell();
    translate([0, pcb_h + 10, 0]) mirror([0, 0, 1]) translate([0, 0, -lid_top]) back_cover();
    translate([pcb_w / 2, -15 - foot_back, 0]) stand();
} else { // assembled
    color("gold") place() orient() front_shell();
    color("white") place() orient() back_cover();
    color("dimgray") stand();
}
