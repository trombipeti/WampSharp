using System;
using System.Linq;
using System.Reactive;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using WampSharp.V2.Rpc;
using TaskExtensions = WampSharp.Core.Utilities.TaskExtensions;

namespace WampSharp.V2.CalleeProxy
{
    internal static class CalleeProxyInterceptorFactory
    {
        public static ICalleeProxyInvocationInterceptor BuildInterceptor(MethodInfo method, ICalleeProxyInterceptor interceptor, WampCalleeProxyInvocationHandler handler)
        {
            Type interceptorType = GetRelevantInterceptorType(method);

            ICalleeProxyInvocationInterceptor result =
                (ICalleeProxyInvocationInterceptor)
                    Activator.CreateInstance(interceptorType,
                                             method,
                                             handler,
                                             interceptor);

            return result;
        }

        private static Type GetRelevantInterceptorType(MethodInfo method)
        {
            Type returnType = method.ReturnType;
            Type genericArgument;
            Type interceptorType;

            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(IObservable<>))
            {
                MethodInfoValidation.ValidateProgressiveObservableMethod(method);

                genericArgument = returnType.GetGenericArguments()[0];
                interceptorType = typeof(ObservableCalleeProxyInterceptor<>);
            }
            else if (!typeof(Task).IsAssignableFrom(returnType))
            {
                MethodInfoValidation.ValidateSyncMethod(method);

                genericArgument = returnType == typeof(void) ? typeof(object) : returnType;
                interceptorType = typeof(SyncCalleeProxyInterceptor<>);
            }
            else
            {
                genericArgument = TaskExtensions.UnwrapReturnType(returnType);

                if (method.IsDefined(typeof(WampProgressiveResultProcedureAttribute)))
                {
                    MethodInfoValidation.ValidateProgressiveMethod(method);
                    interceptorType = typeof(ProgressiveAsyncCalleeProxyInterceptor<,>);
                    Type processGenericArgument = GetProgressType(method);
                    Type result = interceptorType.MakeGenericType(processGenericArgument, genericArgument);
                    return result;
                }
                else
                {
                    MethodInfoValidation.ValidateAsyncMethod(method);
                    interceptorType = typeof(AsyncCalleeProxyInterceptor<>);
                }
            }

            Type closedGenericType = interceptorType.MakeGenericType(genericArgument);
            return closedGenericType;
        }

        private static Type GetProgressType(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            ParameterInfo lastParameter = parameters.LastOrDefault();
            ParameterInfo progressParameter = lastParameter;

            if ((lastParameter != null) &&
                (lastParameter.ParameterType == typeof(CancellationToken)))
            {
                progressParameter =
                    parameters.Take(parameters.Length - 1).LastOrDefault();
            }

            return progressParameter.ParameterType.GetGenericArguments()[0];
        }

    }
}
