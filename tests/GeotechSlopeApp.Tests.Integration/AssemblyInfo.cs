using System.Threading;
using NUnit.Framework;

// Integration tests construct WPF view-models (MeshViewModel, GeometryDefinitionViewModel, ...),
// so run the assembly on an STA thread like the app does.
[assembly: Apartment(ApartmentState.STA)]
