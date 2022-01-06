// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Generic;

namespace Alloy.Api.ViewModels
{
    public class QuestionView
    {
        public QuestionView(Value val)
        {
            Text = val.Text;
            Weight = val.Weight;
        }

        public string Text { get; set; }
        public float Weight { get; set; }
        public string Answer { get; set; }
        public bool IsCorrect { get; set; }
        public bool IsGraded { get; set; }
    }

    public class Value
    {
        public string Answer { get; set; }
        public string Text { get; set; }
        public int Weight { get; set; }
    }

    public class Questions
    {
        public bool Sensitive { get; set; }
        public List<object> Type { get; set; }
        public List<Value> Value { get; set; }
    }

    public class Root
    {
        public Questions Questions { get; set; }
    }
}
