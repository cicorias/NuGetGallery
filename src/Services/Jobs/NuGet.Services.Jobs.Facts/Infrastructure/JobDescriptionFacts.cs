﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace NuGet.Services.Jobs
{
    public class JobDescriptionFacts
    {
        public class TheNameProperty
        {
            [Fact]
            public void GivenAJobWithClassNameEndingJob_ItReturnsThePartBeforeTheWordJob()
            {
                Assert.Equal("ATest", JobDescription.Create(typeof(ATestJob), container: null).Name);
            }

            [Fact]
            public void GivenAJobWithClassNameNotEndingJob_ItReturnsTheWholeTypeName()
            {
                Assert.Equal("ATestJerb", JobDescription.Create(typeof(ATestJerb), container: null).Name);
            }

            [Fact]
            public void GivenAJobWithAttribute_ItReturnsTheNameFromTheAttribute()
            {
                Assert.Equal("ATestJob", JobDescription.Create(typeof(ATestJorb), container: null).Name);
            }

            [Job("ATestJob")]
            public class ATestJorb : JobBase
            {
                public override System.Diagnostics.Tracing.EventSource GetEventSource()
                {
                    throw new NotImplementedException();
                }

                protected internal override Task<InvocationResult> Invoke()
                {
                    throw new NotImplementedException();
                }
            }

            public class ATestJerb : JobBase
            {
                public override System.Diagnostics.Tracing.EventSource GetEventSource()
                {
                    throw new NotImplementedException();
                }

                protected internal override Task<InvocationResult> Invoke()
                {
                    throw new NotImplementedException();
                }
            }

            public class ATestJob : JobBase
            {
                public override System.Diagnostics.Tracing.EventSource GetEventSource()
                {
                    throw new NotImplementedException();
                }

                protected internal override Task<InvocationResult> Invoke()
                {
                    throw new NotImplementedException();
                }
            }
        }
    }
}