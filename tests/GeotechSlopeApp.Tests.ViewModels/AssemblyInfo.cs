using System.Threading;
using NUnit.Framework;

// WPF ViewModels touch System.Windows types and (occasionally) Application.Current.
// Run the entire assembly on an STA thread so those paths behave as they do in the app.
[assembly: Apartment(ApartmentState.STA)]
