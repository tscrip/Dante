// DANTE case, step 1: a box with two press-fit windows for 0.91" SSD1306 128x32 OLEDs.
// All sizes in mm. Measure your modules with calipers and adjust the OLED block if they differ.
// The box is open at the back (a lid comes later). The front wall faces -Y and leans back
// so the screens tilt up toward you.

/* [Box] */
box_w       = 100;  // outside width  (X)
box_h       = 45;   // outside height (Z)
box_d       = 40;   // outside depth at the bottom (Y); the top is shallower because the front leans back
wall        = 2.0;  // side/top/bottom walls
front       = 2.0;  // front wall thickness, measured square to the face (glass is 1.4)
corner_r    = 4;    // outside radius of the vertical corners
front_angle = 80;   // angle between the desk and the front face (90 = upright)
eye_pitch   = 46;   // centre-to-centre distance between the two screens
eye_z       = 0;    // offset of the screens up the face from the middle of the face

/* [OLED module: common 0.91" 128x32] */
pcb_w     = 38.0;  // PCB length (header end included)
pcb_h     = 12.0;  // PCB height
pcb_t     = 1.2;   // PCB thickness
glass_w   = 30.0;  // glass panel width
glass_h   = 11.5;  // glass panel height
glass_x   = -1.5;  // glass centre offset from PCB centre (away from the 4-pin header)

/* [Fit] */
window_clear = 0.20; // per side, glass to window: the glass never takes load
press        = 0.10; // interference per side on the PCB (0.05 tighter printers, 0.15 looser)
rib_w        = 1.2;  // retaining frame wall
rib_depth    = 2.0;  // frame height behind the front wall (>= pcb_t)
bump_r       = 0.6;  // crush-bump radius on the frame (the part that actually grips)

// Mirror the right screen so both 4-pin headers point toward the middle of the box (shorter wires).
// Set to false if you want both modules the same way round.
mirror_right = true;

$fn = 48;

lean      = 90 - front_angle;           // how far the face leans back from vertical
face_len  = box_h / cos(lean);           // length of the front face, bottom edge to top edge
big       = 4 * box_h;                   // long enough to overshoot, trimmed by the slab

assert(box_d - box_h * tan(lean) > 2 * corner_r, "front leans too far for this depth/height");

// Local frame of the front face: origin on the bottom front edge, X along the width,
// Z up the face, +Y square into the box. Everything on the face is modelled here.
module on_face() {
    translate([0, -box_d/2, -box_h/2]) rotate([-lean, 0, 0]) children();
}

// Hull of four vertical-ish posts: two lying in the front face, two vertical at the back.
// `inset` shrinks sides/front, `zlo`/`zhi` trim top and bottom, `back` is the back post y.
module body(inset_side, inset_front, r, zlo, zhi, back) {
    intersection() {
        hull() {
            for (x = [-1, 1]) {
                on_face() translate([x * (box_w/2 - inset_side - r), inset_front + r, -big/2])
                    cylinder(r = r, h = big);
                translate([x * (box_w/2 - inset_side - r), back - r, -big/2])
                    cylinder(r = r, h = big);
            }
        }
        translate([-box_w, -box_d, zlo]) cube([2 * box_w, 2 * box_d, zhi - zlo]);
    }
}

module shell() {
    ri = max(corner_r - wall, 0.5);
    difference() {
        body(0, 0, corner_r, -box_h/2, box_h/2, box_d/2);
        body(wall, front, ri, -box_h/2 + wall, box_h/2 - wall, box_d/2 + 2 * ri + 1); // open back
    }
}

// Glass window through the front wall, screen centred at x = cx (face frame).
module window_cut(cx) {
    translate([cx + glass_x - glass_w/2 - window_clear, -1,
               face_len/2 + eye_z - glass_h/2 - window_clear])
        cube([glass_w + 2 * window_clear, front + 2, glass_h + 2 * window_clear]);
}

// Frame behind the front wall. The pocket is the PCB size plus a small gap, and
// half-round crush bumps reach into it by `press`, so the PCB snaps in and is held by friction.
module press_frame(cx) {
    gap = 0.15;                       // pocket clearance so only the bumps touch
    iw = pcb_w + 2 * gap;  ih = pcb_h + 2 * gap;
    bump_in = gap + press;
    translate([cx, front, face_len/2 + eye_z]) {
        difference() {
            translate([-iw/2 - rib_w, 0, -ih/2 - rib_w]) cube([iw + 2 * rib_w, rib_depth, ih + 2 * rib_w]);
            translate([-iw/2, -0.1, -ih/2]) cube([iw, rib_depth + 1, ih]);
        }
        // crush bumps: two on each long side, one on each short side
        for (fx = [-0.3, 0.3]) for (sz = [-1, 1])
            translate([fx * pcb_w, 0, sz * (ih/2 + bump_r - bump_in)])
                rotate([-90, 0, 0]) cylinder(r = bump_r, h = rib_depth);
        for (sx = [-1, 1])
            translate([sx * (iw/2 + bump_r - bump_in), 0, 0])
                rotate([-90, 0, 0]) cylinder(r = bump_r, h = rib_depth);
    }
}

module case_body() {
    difference() {
        union() {
            shell();
            on_face() for (s = [-1, 1]) press_frame(s * eye_pitch/2);
        }
        on_face() for (s = [-1, 1]) window_cut(s * eye_pitch/2);
    }
}

module left_half() {
    intersection() { case_body(); translate([-box_w, -box_d, -box_h]) cube([box_w, 2 * box_d, 2 * box_h]); }
}

if (mirror_right) {
    left_half();
    mirror([1, 0, 0]) left_half();
} else {
    case_body();
}
