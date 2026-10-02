using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipSim.Core;

public sealed class MajorIncidentSystem
{
    private readonly World _w;
    public MajorIncidentSystem(World w) => _w = w;
    public void Update(float dt) { }
}
