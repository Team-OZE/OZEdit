using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MapPublishingApp.PostProcessing
{
    class FunctionTimings : IPostProcessor
    {
        private const string TEMPLATE_STRING = "//{POST_PROCESSOR_TEMPLATE:FUNCTION_TIMINGS}";
        private readonly Regex REGEX_FUNCTION_REF = new Regex(@"set (?'refFunctionName'[^ ]*)[ ]{0,1}=[ ]{0,1}function (?'targetFunctionName'[^ \n\r]*)", RegexOptions.Compiled);

        private const string TRAMPOLINE_FUNCTION_TEMPLATE_NOINPUT_OUTPUT_NOTHING = @"
        function DebugFunctionTimingsHookedFunction{TARGET_FUNCTION_NAME} takes nothing returns nothing
	        call DebugFunctionTimingsInvokeStart(""{TARGET_FUNCTION_NAME}"")
	        call {TARGET_FUNCTION_NAME}()
            call DebugFunctionTimingsInvokeEnd(""{TARGET_FUNCTION_NAME}"")
        endfunction
        ";


        private const string TRAMPOLINE_FUNCTION_TEMPLATE_NOINPUT_OUTPUT_BOOLEAN = @"
        function DebugFunctionTimingsHookedFunction{TARGET_FUNCTION_NAME} takes nothing returns boolean
            local boolean ret = false
	        call DebugFunctionTimingsInvokeStart(""{TARGET_FUNCTION_NAME}"")
	        set ret = {TARGET_FUNCTION_NAME}()
            call DebugFunctionTimingsInvokeEnd(""{TARGET_FUNCTION_NAME}"")
            return ret
        endfunction
        ";

        private const string HANDLER = @"
        function DebugFunctionTimingsInvokeStart takes string functionName returns nothing
        endfunction

        function DebugFunctionTimingsInvokeEnd takes string functionName returns nothing
        endfunction
        ";

        public void Process(string relativePath, ref StringBuilder fileContent)
        {
            if (relativePath != "war3map.j")
                return;

            var input = fileContent.ToString();

            StringBuilder trampolineFunctionSpace = new StringBuilder();
            trampolineFunctionSpace.Append(HANDLER);

            int i = 0;

            foreach (Match match in REGEX_FUNCTION_REF.Matches(input))
            {
                var refFunctionName = match.Groups["refFunctionName"].Value;
                var targetFunctionName = match.Groups["targetFunctionName"].Value;
                var fullMatch = match.Value;

                // Find matching function definition
                var regexFunctionDefinitionMatch = Regex.Match(input, @"function %FUNCTION_NAME% takes (?'inputParams'.*) returns (?'outputParams'[^\n\r]*)".Replace("%FUNCTION_NAME%", targetFunctionName));
                var inputParams = regexFunctionDefinitionMatch.Groups["inputParams"].Value;
                var outputParams = regexFunctionDefinitionMatch.Groups["outputParams"].Value;

                if (inputParams != "nothing")
                {
                    throw new InvalidOperationException("Unable to handle method with input params: " + targetFunctionName);
                }

                string trampolineFunction;
                switch (outputParams)
                {
                    case "nothing":
                        trampolineFunction = TRAMPOLINE_FUNCTION_TEMPLATE_NOINPUT_OUTPUT_NOTHING.Replace("{TARGET_FUNCTION_NAME}", targetFunctionName);
                        break;
                    case "boolean":
                        trampolineFunction = TRAMPOLINE_FUNCTION_TEMPLATE_NOINPUT_OUTPUT_BOOLEAN.Replace("{TARGET_FUNCTION_NAME}", targetFunctionName);
                        break;
                    default:
                        throw new InvalidOperationException("Unable to handle method " + targetFunctionName + " with output parameter type " + outputParams);
                }

                // Append trampoline function
                trampolineFunctionSpace.Append(trampolineFunction);

                // Replace traget function reference with trampoline function reference
                string hookStatement = fullMatch.Replace("function " + targetFunctionName, "function DebugFunctionTimingsHookedFunction" + targetFunctionName);
                fileContent.Replace(fullMatch, hookStatement);
            }

            fileContent.Replace(TEMPLATE_STRING, trampolineFunctionSpace.ToString());
        }
    }
}
