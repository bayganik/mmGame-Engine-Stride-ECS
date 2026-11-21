using Stride.Core.Mathematics;
using Stride.Engine;

namespace mmCityCenter;

// City components (Stride EntityComponents). The player and
// the camera mode live on entities; the systems in
// CitySystems.cs do the work.
public sealed class PlayerTag : EntityComponent { }

// The player's view state: walk yaw/pitch, and the overview
// camera when F has switched to it.
public sealed class PlayerState : EntityComponent
{
    public float Yaw; // 0 faces -Z (north, up the west road)
    public float Pitch;
    public bool Overview;
    public Vector3 OvTarget = new(80f, 0f, 60f); // the city centre
    public float OvYaw;
    public float OvPitch = -0.55f;
    public float OvRadius = 180f;

    // Driving: walk into a car to get in (the walk system
    // checks the cars' footprints). While driving, the camera
    // sits on top of the car and looks where the car points;
    // W drives forward, A/D steer, S gets out.
    public bool Driving;
    public Entity? DrivenCar;
    public float CarYaw; // the driven car's yaw (0 faces +Z/south)
}

// A parked car (a marker for a future driving system —
// his OpenTK version drives them with CarMovementScript).
public sealed class CarTag : EntityComponent { }
