// DANTE case for the "yellow board" (ESP32-3248S035C, the 3.5" "CYD"), standing at a tilt.
//
// Three printed parts:
//   front  the shell: the board drops in face-down, the screen shows through the window
//   back   screws onto the shell with four M3 flat-head (countersunk) screws, M3x10 (M3x12 also fits);
//          four posts press the board against the front
//   stand  holds the closed case at `tilt` degrees from the desk (60 by default)
//
// The screws go into posts in the top and bottom walls, so they never touch the board. They cut
// their own thread in the plastic; set `inserts = true` to use M3 heat-set inserts instead.
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
wall_x   = 1.8;   // end walls (left and right of the screen)
wall_y   = 7;     // top and bottom walls: thick enough to hold the screws
front    = 1.5;   // front plate around the screen
shell_d  = 12.25; // shell depth, front face to back rim
pad_z    = 4.0;   // the board rests on corner pads this far behind the front face
hole_d   = 2.9;   // holes in the corner pads, under the board's mounting holes
corner_r = 1.5;   // outside corner rounding

/* [Back cover and screws] */
cover_t   = 2.5;   // back cover thickness
post_z    = 6.75;  // the posts touch the back of the board at this depth
rib_z     = 10.25; // stiffening ribs reach down to here
screw_x   = 4;     // screws sit this far in from the pocket's ends, in the middle of the thick walls
pilot_d   = 2.5;   // hole an M3 screw cuts its thread into (2.6 if it is too hard to turn, 2.4 if it strips)
pilot_end = 2;     // the screw holes stop this far behind the front face
inserts   = false; // true: holes for M3 heat-set inserts instead
insert_d  = 4.0;   // hole for the insert (check your inserts' instructions)
insert_l  = 5.0;   // insert length
clear_d   = 3.4;   // screw clearance hole in the back cover
head_d    = 6.2;   // countersink for an M3 flat head (6.0) plus a little room

/* [Stand] */
seat_h     = 3;    // material under the case's lowest corner
rest_len   = 32;   // how far up the back of the case the stand supports it
foot_depth = 60;   // desk footprint behind the case: longer = harder to tip over when you tap the top
tail_h     = 5;    // height of the foot behind the backrest
lip_h      = 2.5;  // lip in front of the screen's bottom edge
lip_t      = 2.4;  // lip thickness
cheek_h    = 8;    // side cheeks hold the case sideways; kept low so the USB plug clears them
cheek_t    = 2.4;  // cheek thickness
fit        = 0.3;  // gap between the case and the stand

$fn = 40;

c = cos(tilt);
s = sin(tilt);
eps = 0.01;
case_d = shell_d + cover_t; // the whole case, front face to back face

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

// The four screws, in the middle of the top and bottom walls near the corners.
screws = [for (x = [screw_x, pcb_w - screw_x], y = [-wall_y / 2, pcb_h + wall_y / 2]) [x, y]];

module rounded_box(x0, y0, x1, y1, z0, z1, r) {
    translate([0, 0, z0]) linear_extrude(z1 - z0)
        offset(r = r) offset(delta = -r) translate([x0, y0]) square([x1 - x0, y1 - y0]);
}

module front_shell() {
    difference() {
        union() {
            difference() {
                rounded_box(-wall_x, -wall_y, pcb_w + wall_x, pcb_h + wall_y, 0, shell_d, corner_r);
                translate([0, 0, front]) cube([pcb_w, pcb_h, shell_d]);        // board pocket
            }
            for (p = pads) translate([p[0], p[2], front - eps]) cube([p[1] - p[0], p[3] - p[2], pad_z - front + eps]);
        }
        translate([win_x, 0, -1]) cube([window_w, pcb_h, front + 2]);            // screen window
        for (h = holes) translate([h[0], h[1], front]) cylinder(d = hole_d, h = pad_z);
        translate([2, 10, -1]) cube([2.5, 4, pad_z + 1]);                        // light sensor slot
        translate([3.9, 44, -1]) cylinder(d = 2.9, h = front + 2);               // small window beside it
        // USB opening in the end wall (rounded slot)
        translate([pcb_w - 1, 38.25, 7]) rotate([0, 90, 0]) hull()
            for (dy = [-3.0, 3.0]) translate([0, dy, 0]) cylinder(d = 3.5, h = wall_x + 2);
        // screw holes
        for (p = screws) translate([p[0], p[1], 0]) {
            translate([0, 0, pilot_end]) cylinder(d = pilot_d, h = shell_d);
            if (inserts) translate([0, 0, shell_d - insert_l - 0.5]) cylinder(d = insert_d, h = insert_l + 1);
        }
    }
}

// ---------------------------------------------------------------------------------------------
// Back cover, same frame. Screws onto the shell's rim; posts press the board into the shell.
// ---------------------------------------------------------------------------------------------

posts = [ // [x0, x1, y0, y1]: where the original's posts touch the board between its parts
    [88.8, 92.8, 2.16, 6.16], [86.8, 90.8, 49.34, 53.34],
    [19, 27, 13.5, 16.5], [12, 20, 39, 42],
];
vents = [ // [x0, x1, y0, y1]: over the ESP32 and the power parts
    [7, 26, 18.5, 35], [77.8, 91.8, 21, 34], [16, 24, 3, 8.5],
];

module back_cover() {
    difference() {
        union() {
            rounded_box(-wall_x, -wall_y, pcb_w + wall_x, pcb_h + wall_y, shell_d, case_d, corner_r);
            for (p = posts) translate([p[0], p[2], post_z]) cube([p[1] - p[0], p[3] - p[2], shell_d - post_z + eps]);
            // stiffening ribs
            for (x = [27, 50.4, 73.8]) translate([x, 0.5, rib_z]) cube([2, pcb_h - 1, shell_d - rib_z + eps]);
            for (y = [7.45, 26.35, 47.27]) translate([27, y, rib_z]) cube([48.8, 2, shell_d - rib_z + eps]);
        }
        // vents as slots, so the cover stays stiff
        for (v = vents) for (y = [v[2] + 1 : 3.5 : v[3] - 1.5])
            translate([v[0], y, shell_d - 1]) linear_extrude(cover_t + 2)
                hull() for (x = [v[0] + 1, v[1] - 1]) translate([x - v[0], 1]) circle(d = 2);
        // screw holes, countersunk so the heads sit flush and the back lies flat on the stand
        for (p = screws) translate([p[0], p[1], 0]) {
            translate([0, 0, shell_d - 1]) cylinder(d = clear_d, h = cover_t + 2);
            translate([0, 0, case_d - head_d / 2 + eps]) cylinder(d1 = 0, d2 = head_d, h = head_d / 2);
        }
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
                [0, c, s, wall_y * c - case_d * s],
                [0, s, -c, seat_h + wall_y * s + case_d * c],
                [0, 0, 0, 1]]) children();
}

// Side view of the stand, in (Y, Z) world coordinates. V is where the case's back-bottom edge sits.
function v2(a, b) = [a[0] + b[0], a[1] + b[1]];
function sc(v, k) = [v[0] * k, v[1] * k];
U  = [c, s];        // up the face
NI = [-s, c];       // out of the screen (forward and up)
V  = [0, seat_h];
F  = v2(V, sc(NI, case_d + fit));           // just in front of the case's front-bottom edge
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
    h = wall_y + cheek_h;
    // the extra offsets merge the two outlines cleanly (no hairline slivers where their edges meet)
    offset(delta = -0.05) offset(delta = 0.05) union() {
        stand_profile();
        polygon([F, V, v2(V, sc(U, h)), v2(F, sc(U, h))]);
    }
}

module stand() {
    inner = pcb_w + 2 * wall_x + 2 * fit;
    outer = inner + 2 * cheek_t;
    rotate([90, 0, 90]) {
        translate([0, 0, -inner / 2]) linear_extrude(inner) stand_profile();
        for (z = [-outer / 2, inner / 2 - eps]) translate([0, 0, z]) linear_extrude(cheek_t + eps) cheek_profile();
    }
}

assert(T2[0] < foot_back, "the foot is too short for this tilt and backrest");

// ---------------------------------------------------------------------------------------------

if (part == "front") front_shell();
else if (part == "back") mirror([0, 0, 1]) translate([0, 0, -case_d]) back_cover();
else if (part == "stand") stand();
else if (part == "print") {
    front_shell();
    translate([0, pcb_h + 2 * wall_y + 6, 0]) mirror([0, 0, 1]) translate([0, 0, -case_d]) back_cover();
    translate([pcb_w / 2, -15 - foot_back, 0]) stand();
} else { // assembled
    color("gold") place() orient() front_shell();
    color("white") place() orient() back_cover();
    color("dimgray") stand();
}
