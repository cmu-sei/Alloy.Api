// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoFixture;
using AutoFixture.AutoFakeItEasy;
using Alloy.Api.Tests.Shared.Fixtures;

namespace Alloy.Api.Tests.Unit.Fixtures;

public static class AlloyFixtureFactory
{
    public static IFixture CreateFixture()
    {
        return new Fixture()
            .Customize(new AutoFakeItEasyCustomization())
            .Customize(new AlloyCustomization());
    }
}
