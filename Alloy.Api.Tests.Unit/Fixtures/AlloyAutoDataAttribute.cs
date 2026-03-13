// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using AutoFixture;
using AutoFixture.AutoFakeItEasy;
using AutoFixture.Xunit2;
using Alloy.Api.Tests.Shared.Fixtures;

namespace Alloy.Api.Tests.Unit.Fixtures;

public class AlloyAutoDataAttribute : AutoDataAttribute
{
    private static readonly IFixture FIXTURE = new Fixture()
        .Customize(new AutoFakeItEasyCustomization())
        .Customize(new AlloyCustomization());

    public AlloyAutoDataAttribute() : base(() => FIXTURE) { }
}
