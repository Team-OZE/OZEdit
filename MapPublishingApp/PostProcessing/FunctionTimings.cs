using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MapPublishingApp.PostProcessing
{
    class FunctionTimings : IPostProcessor
    {
        private const string TEMPLATE_STRING = "//{POST_PROCESSOR_TEMPLATE:FUNCTION_TIMINGS}";
        private const string NB_FUNCTION_IDENTIFIERS_SETTER = "set debug_functiontimings_nb_functionidentifiers=420";
        private readonly Regex REGEX_FUNCTION_REF = new Regex(@"set (?'refFunctionName'[^ ]*)[ ]{0,1}=[ ]{0,1}function (?'targetFunctionName'[^ \n\r]*)", RegexOptions.Compiled);

        private const string TRAMPOLINE_FUNCTION_TEMPLATE_NOINPUT_OUTPUT_NOTHING = @"
        function DebugFunctionTimingsHookedFunction{TARGET_FUNCTION_NAME} takes nothing returns nothing
	        call DebugFunctionTimingsInvokeStart(""{TARGET_FUNCTION_NAME}"", {FUNCTION_IDENTIFIER})
	        call {TARGET_FUNCTION_NAME}()
            call DebugFunctionTimingsInvokeEnd(""{TARGET_FUNCTION_NAME}"", {FUNCTION_IDENTIFIER})
        endfunction
        ";


        private const string TRAMPOLINE_FUNCTION_TEMPLATE_NOINPUT_OUTPUT_BOOLEAN = @"
        function DebugFunctionTimingsHookedFunction{TARGET_FUNCTION_NAME} takes nothing returns boolean
            local boolean ret = false
	        call DebugFunctionTimingsInvokeStart(""{TARGET_FUNCTION_NAME}"", {FUNCTION_IDENTIFIER})
	        set ret = {TARGET_FUNCTION_NAME}()
            call DebugFunctionTimingsInvokeEnd(""{TARGET_FUNCTION_NAME}"", {FUNCTION_IDENTIFIER})
            return ret
        endfunction
        ";

        public void Process(string relativePath, ref StringBuilder fileContent)
        {
            if (relativePath != "war3map.j")
                return;

            var input = fileContent.ToString();

            StringBuilder trampolineFunctionSpace = new StringBuilder();

            int function_identifier = 1;

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
                        trampolineFunction = TRAMPOLINE_FUNCTION_TEMPLATE_NOINPUT_OUTPUT_NOTHING.Replace("{TARGET_FUNCTION_NAME}", targetFunctionName).Replace("{FUNCTION_IDENTIFIER}", function_identifier.ToString());
                        break;
                    case "boolean":
                        trampolineFunction = TRAMPOLINE_FUNCTION_TEMPLATE_NOINPUT_OUTPUT_BOOLEAN.Replace("{TARGET_FUNCTION_NAME}", targetFunctionName).Replace("{FUNCTION_IDENTIFIER}", function_identifier.ToString());
                        break;
                    default:
                        throw new InvalidOperationException("Unable to handle method " + targetFunctionName + " with output parameter type " + outputParams);
                }

                // Append trampoline function
                trampolineFunctionSpace.Append(trampolineFunction);

                // Replace traget function reference with trampoline function reference
                string hookStatement = fullMatch.Replace("function " + targetFunctionName, "function DebugFunctionTimingsHookedFunction" + targetFunctionName);
                fileContent.Replace(fullMatch, hookStatement);

                function_identifier += 1;
            }

            fileContent.Replace(NB_FUNCTION_IDENTIFIERS_SETTER, NB_FUNCTION_IDENTIFIERS_SETTER.Replace("420", function_identifier.ToString()));
            fileContent.Replace(TEMPLATE_STRING, trampolineFunctionSpace.ToString());
        }
    }
}
