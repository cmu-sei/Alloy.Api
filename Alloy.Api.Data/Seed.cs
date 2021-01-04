// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using Alloy.Api.Data.Models;

namespace Alloy.Api.Data
{
    public class Seed
    {
        public static void Run(AlloyContext context)
        {
            // player view ID fc41c788-063b-4018-9f28-5f68a52f4e76
            // caster directory ID 0cec99ba-26a5-4825-b2e2-b91b493931b5
            // steamfitter scenarioTemplate ID 9fd3c38e-58b0-4af1-80d1-1895af91f1f9
            var mccorc1 = new EventTemplateEntity()
            {
                Id = Guid.Parse("930bec74-0c28-40ad-811e-b1b9a7b9b00e"),
                ViewId = Guid.Parse("fc41c788-063b-4018-9f28-5f68a52f4e76"),
                DirectoryId = Guid.Parse("0cec99ba-26a5-4825-b2e2-b91b493931b5"),
                ScenarioTemplateId = Guid.Parse("21ab824c-990b-4067-9baf-c6f3036ac116"),
                Name = "MCCORC Lab 1",
                Description = "Marine Corps Cyber Readiness Curriculum - Lab 1",
                DurationHours = 2
            };
            context.EventTemplates.Add(mccorc1);

            context.SaveChanges();
            Console.WriteLine("Seed of data has completed.");
        }
    }
}

