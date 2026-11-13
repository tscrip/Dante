// DANTE case, step 1: a box with two press-fit windows for 0.91" SSD1306 128x32 OLEDs.
// All sizes in mm. Measure your modules with calipers and adjust the OLED block if they differ.
// The box is open at the back (a lid comes later). The front wall faces -Y.

/* [Box] */
box_w     = 100;   // outside width  (X)
box_h     = 45;    // outside height (Z)
box_d     = 40;    // outside depth  (Y)
wall      = 2.0;   // side/top/bottom walls
front     = 2.0;   // front wall thickness (glass is 1.4, so it sits ~0.6 behind the face)
corner_r  = 4;     // outside corner radius
eye_pitch = 46;    // centre-to-centre distance between the two screens
eye_z     = 0;     // vertical offset of the screens from the box centre

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
rib_depth    = 2.0;  // frame height behind the front wall (≥ pcb_t)
bump_r       = 0.6;  // crush-bump radius on the frame (the part that actually grips)

$fn = 48;

module rounded_box(w, d, h, r) {
    hull() for (x = [-w/2 + r, w/2 - r], y = [-d/2 + r, d/2 - r])
        translate([x, y, -h/2]) cylinder(r = r, h = h);
}

// Shell: closed front, open back.
module shell() {
    difference() {
        rounded_box(box_w, box_d, box_h, corner_r);
        translate([0, front, 0])
            rounded_box(box_w - 2*wall, box_d, box_h - 2*wall, max(corner_r - wall, 0.5));
    }
}

// Glass window through the front wall, for one screen centred at x = cx.
module window_cut(cx) {
    translate([cx + glass_x - glass_w/2 - window_clear, -box_d/2 - 1,
               eye_z - glass_h/2 - window_clear])
        cube([glass_w + 2*window_clear, front + 2, glass_h + 2*window_clear]);
}

// Frame behind the front wall. The pocket is the PCB size plus a small gap, and
// half-round crush bumps reach into it by `press`, so the PCB snaps in and is held by friction.
module press_frame(cx) {
    gap = 0.15;                       // pocket clearance so only the bumps touch
    iw = pcb_w + 2*gap;  ih = pcb_h + 2*gap;
    y0 = -box_d/2 + front;            // inside face of the front wall
    translate([cx, y0, eye_z]) {
        difference() {
            translate([-iw/2 - rib_w, 0, -ih/2 - rib_w]) cube([iw + 2*rib_w, rib_depth, ih + 2*rib_w]);
            translate([-iw/2, -0.1, -ih/2]) cube([iw, rib_depth + 1, ih]);
        }
        // crush bumps: two on each long side, one on each short side
        bump_in = gap + press;
        for (fx = [-0.3, 0.3])
            for (sz = [-1, 1])
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
            for (s = [-1, 1]) press_frame(s * eye_pitch/2);
        }
        for (s = [-1, 1]) window_cut(s * eye_pitch/2);
    }
}

// Mirror the right screen so both 4-pin headers point toward the middle of the box (shorter wires).
// Set to false if you want both modules the same way round.
mirror_right = true;

if (mirror_right) {
    // left half as modelled, right half mirrored
    intersection() { case_body(); translate([-box_w, -box_d, -box_h]) cube([box_w, 2*box_d, 2*box_h]); }
    mirror([1, 0, 0])
    intersection() { case_body(); translate([-box_w, -box_d, -box_h]) cube([box_w, 2*box_d, 2*box_h]); }
} else {
    case_body();
}
