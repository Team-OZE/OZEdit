﻿using System;
using System.Collections.Generic;
using System.Text;

namespace MapPublishingApp.PostProcessing
{
    public interface IPostProcessor
    {
        public void Process(string relativePath, ref StringBuilder fileContent);
    }
}
