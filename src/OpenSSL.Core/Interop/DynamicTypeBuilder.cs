﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Diagnostics;

using OpenSSL.Core.Interop.Attributes;
using OpenSSL.Core.Interop.SafeHandles;

namespace OpenSSL.Core.Interop
{
    //TODO: allow multiple generic parameters
    public static class DynamicTypeBuilder
    {
        private static ModuleBuilder moduleBuilder;
        /*
         * Tuple.Item1 -> takeOwnerShip: true, isNew: true
         * Tuple.Item2 -> takeOwnerShip: false, isNew: false
         * Tuple.Item3 -> takeOwnerShip: true, isNew: false
        */
        private static Dictionary<string, Tuple<Type, Type, Type>> dictConcreteTypes;

        static DynamicTypeBuilder()
        {
            AssemblyName asmName = new AssemblyName("OpenSSL.Core.Interop.Dynamic");
            AssemblyBuilder asmBuilder = AssemblyBuilder.DefineDynamicAssembly(asmName, AssemblyBuilderAccess.Run);
            moduleBuilder = asmBuilder.DefineDynamicModule(asmName.Name);

            dictConcreteTypes = new Dictionary<string, Tuple<Type, Type, Type>>();
        }

        internal static Type CreateOpenSSLWrapper<TInterface>(string dllName)
        {
            if (!typeof(TInterface).GetTypeInfo().IsInterface)
                throw new InvalidOperationException("Only interfaces allowed");

            TypeBuilder typeBuilder = moduleBuilder.DefineType(typeof(TInterface).Name.Substring(1), TypeAttributes.Public);
            typeBuilder.AddInterfaceImplementation(typeof(TInterface));

            MethodInfo[] interfaceMethod = typeof(TInterface).GetTypeInfo().GetMethods();
            foreach (MethodInfo ifMethod in interfaceMethod)
            {
                string name = ifMethod.Name;
                try
                {
                    CreateInterfaceImplementation(dllName, typeBuilder, ifMethod);
                }
                catch (Exception ex)
                {
                    Debug.Write(ex.Message);
                    Debug.Write(ex.StackTrace);
                    throw;
                }
            }

            Type type = null;
            try
            {
                type = typeBuilder.CreateTypeInfo().AsType();
            }
            catch (Exception ex)
            {
                Debug.Write(ex.Message);
                Debug.Write(ex.StackTrace);
                throw;
            }

            return type;
        }

        internal static Type GetConcreteNewType<T>()
            where T : SafeBaseHandle
        {
            Type abstractType = typeof(T);

            if (!abstractType.IsAbstract)
                throw new InvalidOperationException("This operation is only supported for abstarct type");

            AddConcreteType(abstractType);

            return dictConcreteTypes[abstractType.Name].Item1;
        }

        #region Native implementation generator
        private static void CreateInterfaceImplementation(string dllName, TypeBuilder typeBuilder, MethodInfo ifMethod)
        {
            CustomAttributeBuilder attrBuilder;
            Type attrType;
            Type[] ctorParams;
            ConstructorInfo ctor;
            MethodBuilder nativeMethod, interfaceMethod;
            ILGenerator interfaceMethodIL;
            MethodInfo postConstruction, checkIntegerReturn, checkSafeHandleReturn, getHandleMethod;
            LocalBuilder[] nativeLocals;
            LocalBuilder[] constructedLocals;
            LocalBuilder currentLocal;
            Type concreteReturnType, concreteGenericType;
            LocalBuilder returnLocal = null;

            //get interface method info
            string methodName = ifMethod.Name;

            postConstruction = typeof(SafeBaseHandle).GetMethod("PostConstruction", BindingFlags.NonPublic | BindingFlags.Instance);
            checkIntegerReturn = typeof(Native).GetMethod("ExpectSuccess", BindingFlags.Public | BindingFlags.Static);
            checkSafeHandleReturn = typeof(Native).GetMethod("ExpectNonNull", BindingFlags.Public | BindingFlags.Static);
            getHandleMethod = typeof(SafeHandle).GetMethod("DangerousGetHandle");

            //declare native method
            nativeMethod = GenerateNativeParameterTypes(typeBuilder, ifMethod, out Type nativeReturnType, out Type[] nativeParameterTypes);

            //declare native method DllImportAttribute
            attrType = typeof(DllImportAttribute);
            ctorParams = new Type[] { typeof(string) };
            ctor = attrType.GetTypeInfo().GetConstructor(ctorParams);
            attrBuilder = new CustomAttributeBuilder(ctor, new object[] { dllName },
                new FieldInfo[] { attrType.GetTypeInfo().GetField("EntryPoint") },
                new object[] { methodName });
            nativeMethod.SetCustomAttribute(attrBuilder);

            interfaceMethod = GenerateInterfaceParameterTypes(typeBuilder, ifMethod, nativeParameterTypes, out Type interfaceReturnType, out Type[] interfaceParameterTypes);
            interfaceMethodIL = interfaceMethod.GetILGenerator();

            //if native method has a return type, define a local and a label
            if (interfaceReturnType != typeof(void))
                interfaceMethodIL.DeclareLocal(interfaceMethod.ReturnType); //this is loc_0

            if (interfaceReturnType != nativeReturnType && (interfaceReturnType.IsGenericParameter || interfaceReturnType.IsGenericType))
                returnLocal = interfaceMethodIL.DeclareLocal(nativeReturnType);

            //declare out/IntPtr types as locals
            nativeLocals = new LocalBuilder[nativeParameterTypes.Length];
            for (int i = 0; i < nativeParameterTypes.Length; i++)
            {
                if (interfaceParameterTypes[i] != nativeParameterTypes[i])
                    nativeLocals[i] = interfaceMethodIL.DeclareLocal(
                        nativeParameterTypes[i].IsByRef
                            ? nativeParameterTypes[i].GetElementType()
                            : nativeParameterTypes[i]);
            }

            //load pointer from generic parameters
            for (int i = 0; i < nativeParameterTypes.Length; i++)
            {
                if (nativeParameterTypes[i] == interfaceParameterTypes[i])
                    continue;

                if (nativeParameterTypes[i] != typeof(IntPtr))
                    continue;

                EmitLoadArgument(i, interfaceMethodIL);
                interfaceMethodIL.Emit(OpCodes.Call, getHandleMethod);
                interfaceMethodIL.Emit(OpCodes.Stloc_S, nativeLocals[i]);
            }

            //load correct parameters for native call
            for (int i = 0; i < interfaceParameterTypes.Length; i++)
            {
                if (interfaceParameterTypes[i] == nativeParameterTypes[i])
                    EmitLoadArgument(i, interfaceMethodIL);
                else if (nativeParameterTypes[i].IsByRef)
                    interfaceMethodIL.Emit(OpCodes.Ldloca_S, nativeLocals[i]);
                else
                    interfaceMethodIL.Emit(OpCodes.Ldloc_S, nativeLocals[i]);
            }

            interfaceMethodIL.Emit(OpCodes.Call, nativeMethod);

            //store return type
            if (returnLocal != null)
                interfaceMethodIL.Emit(OpCodes.Stloc_S, returnLocal);
            else if (interfaceReturnType != typeof(void))
                interfaceMethodIL.Emit(OpCodes.Stloc_0);

            //construct concrete return type
            //returns a SafeStackHandle<>
            if (interfaceReturnType.IsGenericType
                && interfaceReturnType.ContainsGenericParameters
                && CreateConcreteType(ifMethod.ReturnParameter, out concreteReturnType))
            {
                CreateConcreteGenericReturnType(
                    interfaceMethod,
                    interfaceMethodIL,
                    returnLocal,
                    concreteReturnType,
                    concreteReturnType.Name.EndsWith("own") ? false : true,
                    concreteReturnType.Name.EndsWith("new") ? true : false);
            }
            //returns an IStackable (from a SafeStackHandle<>)
            else if (interfaceReturnType.IsGenericParameter)
            {
                CreateConcreteStackableReturnType(
                    interfaceMethod,
                    interfaceMethodIL,
                    returnLocal,
                    interfaceParameterTypes[0], //is the SafeStackHandle<>, should always be the first parameter
                    ifMethod.ReturnType.GetCustomAttribute<DontTakeOwnershipAttribute>() == null);
            }

            //store concrete return type
            if (returnLocal != null)
                interfaceMethodIL.Emit(OpCodes.Stloc_0);

            //create and store constructed generic out parameters
            constructedLocals = new LocalBuilder[nativeParameterTypes.Length];
            for (int i = 0; i < interfaceParameterTypes.Length; i++)
            {
                if (nativeLocals[i] == null || nativeLocals[i].LocalType != typeof(IntPtr))
                    continue;

                if (!interfaceParameterTypes[i].IsByRef)
                    continue;

                if (!CreateConcreteType(ifMethod.GetParameters()[i], out concreteGenericType))
                    continue;

                concreteGenericType = concreteGenericType.GetElementType();

                if (interfaceParameterTypes[i].GetElementType().ContainsGenericParameters)
                    concreteGenericType = concreteGenericType.GetGenericTypeDefinition().MakeGenericType(interfaceMethod.GetGenericArguments()[0]);
                else
                    concreteGenericType = concreteGenericType.MakeGenericType(interfaceParameterTypes[i].GetElementType().GetGenericArguments()[0]);

                constructedLocals[i] = interfaceMethodIL.DeclareLocal(concreteGenericType);

                CreateConcreteGenericParameterType(
                    interfaceMethod,
                    interfaceMethodIL,
                    i,
                    concreteGenericType,
                    nativeLocals,
                    constructedLocals);
            }

            //store out parameters
            for (int i = 0; i < interfaceParameterTypes.Length; i++)
            {
                if (nativeParameterTypes[i] == interfaceParameterTypes[i])
                    continue;

                if (!interfaceParameterTypes[i].IsByRef)
                    continue;

                //execute postConstruction on constructed type
                if (nativeLocals[i].LocalType == typeof(IntPtr))
                    interfaceMethodIL.Emit(OpCodes.Ldloc_S, currentLocal = constructedLocals[i]);
                else
                    interfaceMethodIL.Emit(OpCodes.Ldloc_S, currentLocal = nativeLocals[i]);
                interfaceMethodIL.Emit(OpCodes.Callvirt, postConstruction);

                //store reference
                EmitLoadArgument(i, interfaceMethodIL);
                interfaceMethodIL.Emit(OpCodes.Ldloc_S, currentLocal);
                interfaceMethodIL.Emit(OpCodes.Stind_Ref);
            }

            //load return value
            if (interfaceReturnType != typeof(void))
            {
                interfaceMethodIL.Emit(OpCodes.Ldloc_0);

                //check return value
                if (ifMethod.GetCustomAttribute<DontCheckReturnTypeAttribute>() is null)
                {
                    //check return values and throw exception if necessary
                    if (interfaceReturnType == typeof(int)) //TODO: uint/ulong/long
                    {
                        interfaceMethodIL.Emit(OpCodes.Call, checkIntegerReturn);
                        interfaceMethodIL.Emit(OpCodes.Ldloc_0);
                    }
                    else if (IsSafeHandle<SafeHandle>(interfaceReturnType))
                    {
                        interfaceMethodIL.Emit(OpCodes.Call, checkSafeHandleReturn);
                        interfaceMethodIL.Emit(OpCodes.Ldloc_0);
                    }
                }

                //always consider SafeBaseHandle as the (in)correct implementation (can be a generic parameter
                if (IsSafeHandle<SafeBaseHandle>(interfaceReturnType))
                {
                    interfaceMethodIL.Emit(OpCodes.Callvirt, postConstruction);
                    interfaceMethodIL.Emit(OpCodes.Ldloc_0);
                }
            }

            interfaceMethodIL.Emit(OpCodes.Ret);

            //define interface implementation as the interface override
            typeBuilder.DefineMethodOverride(interfaceMethod, ifMethod);
        }

        private static MethodBuilder GenerateNativeParameterTypes(
            TypeBuilder typeBuilder,
            MethodInfo originalInterfaceMethod,
            out Type nativeReturnType,
            out Type[] nativeParameterTypes)
        {
            Type originalReturnType = originalInterfaceMethod.ReturnType;
            ParameterInfo[] originalInterfaceParameters = originalInterfaceMethod.GetParameters();
            Type parameterType, concreteType;

            //define return type as IntPtr if generic or generic parameter
            if (originalReturnType.IsGenericType || originalReturnType.IsGenericParameter)
                nativeReturnType = typeof(IntPtr);
            else if (CreateConcreteType(originalInterfaceMethod.ReturnParameter, out concreteType))
                nativeReturnType = concreteType;
            else
                nativeReturnType = originalInterfaceMethod.ReturnType;

            nativeParameterTypes = new Type[originalInterfaceParameters.Length];
            Type refParameterType;
            for (int i = 0; i < originalInterfaceParameters.Length; i++)
            {
                parameterType = originalInterfaceParameters[i].ParameterType;
                if (parameterType.IsByRef)
                {
                    refParameterType = parameterType.GetElementType();
                    if (refParameterType.IsGenericType || refParameterType.IsGenericParameter)
                        nativeParameterTypes[i] = typeof(IntPtr).MakeByRefType();
                    else if (CreateConcreteType(originalInterfaceParameters[i], out concreteType))
                        nativeParameterTypes[i] = concreteType;
                    else
                        nativeParameterTypes[i] = parameterType;
                }
                else if (parameterType.IsGenericType || parameterType.IsGenericParameter)
                    nativeParameterTypes[i] = typeof(IntPtr);
                else
                    nativeParameterTypes[i] = parameterType;
            }

            MethodBuilder nativeMethod = typeBuilder.DefineMethod(
                string.Format("openssl_{0}", originalInterfaceMethod.Name),
                MethodAttributes.Private | MethodAttributes.PinvokeImpl | MethodAttributes.Static,
                nativeReturnType,
                nativeParameterTypes);

            ParameterBuilder parBuilder;
            for (int i = 0; i < nativeParameterTypes.Length; i++)
            {
                if (!nativeParameterTypes[i].IsByRef)
                    continue;

                parBuilder = nativeMethod.DefineParameter(i + 1, ParameterAttributes.Out, originalInterfaceParameters[i].Name);
            }

            return nativeMethod;
        }

        private static MethodBuilder GenerateInterfaceParameterTypes(
            TypeBuilder typeBuilder,
            MethodInfo originalInterfaceMethod,
            Type[] nativeParameterTypes,
            out Type interfaceReturnType,
            out Type[] interfaceParameterTypes)
        {
            Type originalReturnType = originalInterfaceMethod.ReturnType;
            ParameterInfo[] originalInterfaceParameters = originalInterfaceMethod.GetParameters();
            Type[] originalInterfaceParameterTypes = originalInterfaceParameters.Select(x => x.ParameterType).ToArray();

            //from C# 7.2: add required attributes (in IL .param followed by .custom)
            Type[][] requiredModifiers = originalInterfaceParameters.Select(x => x.GetRequiredCustomModifiers()).ToArray();

            //declare interface method
            MethodBuilder interfaceMethod = typeBuilder.DefineMethod(
                originalInterfaceMethod.Name,
                MethodAttributes.Public | MethodAttributes.Virtual,
                CallingConventions.Standard,
                originalReturnType,
                null,
                null,
                originalInterfaceParameterTypes,
                requiredModifiers,
                null);

            GenericTypeParameterBuilder[] genericParameters = null;
            if (originalInterfaceMethod.IsGenericMethod)
            {
                //generate generic parameters
                CreateMethodGenericParameter(interfaceMethod, originalInterfaceMethod, out genericParameters);

                //replace return type with correct generic parameterized type
                if (originalReturnType.IsGenericType)
                    interfaceMethod.SetReturnType(originalReturnType.GetGenericTypeDefinition().MakeGenericType(genericParameters[0]));
                //return type if of generic parameter type
                else if (originalReturnType.IsGenericParameter)
                    interfaceMethod.SetReturnType(genericParameters[0]);
            }

            //interfaceParameterTypes = new Type[originalInterfaceParameterTypes.Length];
            //for(int i = 0; i < originalInterfaceParameterTypes.Length; i++)
            //{
            //    if (!originalInterfaceParameterTypes[i].ContainsGenericParameters)
            //        interfaceParameterTypes[i] = originalInterfaceParameterTypes[i];
            //    else
            //        interfaceParameterTypes[i] = originalInterfaceParameterTypes[i].GetGenericTypeDefinition().MakeGenericType(genericParameters[0]);
            //}
            //interfaceMethod.SetParameters(interfaceParameterTypes);

            ParameterBuilder parBuilder;
            for (int i = 0; i < originalInterfaceParameterTypes.Length; i++)
            {
                if (!originalInterfaceParameterTypes[i].IsByRef)
                    continue;

                parBuilder = interfaceMethod.DefineParameter(i + 1, ParameterAttributes.Out, originalInterfaceParameters[i].Name);
            }

            interfaceReturnType = interfaceMethod.ReturnType;
            interfaceParameterTypes = originalInterfaceParameterTypes;
            return interfaceMethod;
        }

        private static void CreateMethodGenericParameter(MethodBuilder methodBuilder, MethodInfo method, out GenericTypeParameterBuilder[] genericParameters)
        {
            Type[] genericArguments = method.GetGenericArguments();
            genericParameters = methodBuilder.DefineGenericParameters(genericArguments.Select(x => string.Concat(x.Name, "_new")).ToArray());

            AssingGenericParameterConstraints(genericArguments, genericParameters);
        }

        private static void AssingGenericParameterConstraints(Type[] genericArguments, GenericTypeParameterBuilder[] genericParameters)
        {
            if (genericArguments.Length > 1)
                throw new InvalidOperationException("Only supports up to 1 generic parameter");

            for (int i = 0; i < genericArguments.Length; i++)
            {
                foreach (Type t in genericArguments[i].GetGenericParameterConstraints())
                {
                    if (t.IsInterface)
                        genericParameters[i].SetInterfaceConstraints(t);
                    else if (t.IsAbstract)
                        genericParameters[i].SetBaseTypeConstraint(t);
                    else
                        throw new InvalidOperationException("Invalid generic constraint type");
                }
            }
        }

        //only pass actual parameters from ifMethod interface decleration
        private static bool CreateConcreteType(ParameterInfo parameter, out Type concreteType)
        {
            concreteType = parameter.ParameterType;
            NewSafeHandleAttribute newAttr;
            DontTakeOwnershipAttribute ownAttr;
            Type parameterType;

            if (parameter.ParameterType.IsByRef)
                parameterType = parameter.ParameterType.GetElementType();
            else
                parameterType = parameter.ParameterType;

            if (!(parameterType.IsAbstract && IsSafeHandle<SafeBaseHandle>(parameterType)))
                return false;

            AddConcreteType(parameterType);

            if (!((newAttr = parameter.GetCustomAttribute<NewSafeHandleAttribute>()) is null))
                concreteType = dictConcreteTypes[parameterType.Name].Item1;
            else if (!((ownAttr = parameter.GetCustomAttribute<DontTakeOwnershipAttribute>()) is null))
                concreteType = dictConcreteTypes[parameterType.Name].Item2;
            else
                concreteType = dictConcreteTypes[parameterType.Name].Item3;

            if (parameter.ParameterType.IsByRef)
                concreteType = concreteType.MakeByRefType();

            return true;
        }

        private static void AddConcreteType(Type abstractType)
        {
            TypeBuilder typeBuilderNew, typeBuilderOwn, typeBuilderRef;
            ConstructorInfo baseCtor, ptrCtor = null;
            Type genericType = null;

            if (dictConcreteTypes.ContainsKey(abstractType.Name))
                return;

            if (abstractType.IsGenericType)
            {
                genericType = abstractType.GetGenericTypeDefinition();

                if (genericType != typeof(SafeStackHandle<>))
                    throw new InvalidOperationException("Unknown generic type");

                baseCtor = genericType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(bool), typeof(bool) }, null);
            }
            else
                baseCtor = abstractType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(bool), typeof(bool) }, null);

            ptrCtor = typeof(SafeBaseHandle).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(IntPtr), typeof(bool), typeof(bool) }, null);

            typeBuilderNew = CreateConcreteTypeWithDefaultConstructor(abstractType, "new", baseCtor, genericType ?? abstractType, true, true);
            CreatePtrConstructor(typeBuilderNew, ptrCtor);

            typeBuilderOwn = CreateConcreteTypeWithDefaultConstructor(abstractType, "own", baseCtor, genericType ?? abstractType, false, false);
            CreatePtrConstructor(typeBuilderOwn, ptrCtor);

            typeBuilderRef = CreateConcreteTypeWithDefaultConstructor(abstractType, "ref", baseCtor, genericType ?? abstractType, true, false);
            CreatePtrConstructor(typeBuilderRef, ptrCtor);

            dictConcreteTypes.Add(abstractType.Name, Tuple.Create<Type, Type, Type>
                (
                    typeBuilderNew.CreateTypeInfo().AsType(),
                    typeBuilderOwn.CreateTypeInfo().AsType(),
                    typeBuilderRef.CreateTypeInfo().AsType()
                ));
        }

        private static bool IsSafeHandle<T>(Type type)
            where T : SafeHandle
        {
            Type currentType = type;
            while (currentType.BaseType != null)
            {
                currentType = currentType.BaseType;
                if (currentType.Equals(typeof(T)))
                    return true;
            }

            return false;
        }

        private static TypeBuilder CreateConcreteTypeWithDefaultConstructor(
            Type parameterType,
            string nameSuffix,
            ConstructorInfo baseCtor,
            Type baseType,
            bool takeOwnership,
            bool isNew
            )
        {
            TypeBuilder typeBuilder = moduleBuilder.DefineType($"{parameterType.Name}_{nameSuffix}", TypeAttributes.NotPublic, baseType);

            if (baseType.IsGenericType && baseType.ContainsGenericParameters)
                CreateClassGenericParameter(typeBuilder, baseType);

            //define default constructor
            ConstructorBuilder ctorBuilder = typeBuilder.DefineConstructor(MethodAttributes.Private, CallingConventions.Standard, null);
            ILGenerator ctorIL = ctorBuilder.GetILGenerator();

            ctorIL.Emit(OpCodes.Ldarg_0);
            ctorIL.Emit(takeOwnership ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            ctorIL.Emit(isNew ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            ctorIL.Emit(OpCodes.Call, baseCtor);
            ctorIL.Emit(OpCodes.Ret);

            return typeBuilder;
        }

        private static void CreateClassGenericParameter(TypeBuilder typeBuilder, Type genericTypeDefenition)
        {
            Type[] genericArguments = genericTypeDefenition.GetGenericArguments();
            GenericTypeParameterBuilder[] genericParameters = typeBuilder.DefineGenericParameters(genericArguments.Select(x => x.Name).ToArray());

            AssingGenericParameterConstraints(genericArguments, genericParameters);
        }

        private static void CreatePtrConstructor(TypeBuilder typeBuilder, ConstructorInfo ptrCtor)
        {
            ConstructorBuilder ctorBuilder = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, new Type[] { typeof(IntPtr), typeof(bool), typeof(bool) });
            ILGenerator ctorIL = ctorBuilder.GetILGenerator();
            ctorIL.Emit(OpCodes.Ldarg_0);
            ctorIL.Emit(OpCodes.Ldarg_1);
            ctorIL.Emit(OpCodes.Ldarg_2);
            ctorIL.Emit(OpCodes.Ldarg_3);
            ctorIL.Emit(OpCodes.Call, ptrCtor);
            ctorIL.Emit(OpCodes.Ret);
        }

        //load arguments (as instance you need to skip Ldarg_0)
        private static void EmitLoadArgument(int argumentPosition, ILGenerator ilGen)
        {
            switch (argumentPosition)
            {
                case 0:
                    ilGen.Emit(OpCodes.Ldarg_1);
                    break;
                case 1:
                    ilGen.Emit(OpCodes.Ldarg_2);
                    break;
                case 2:
                    ilGen.Emit(OpCodes.Ldarg_3);
                    break;
                default:
                    ilGen.Emit(OpCodes.Ldarg_S, (argumentPosition + 1));
                    break;
            }
        }

        //create a SafeStackHandle<>
        private static void CreateConcreteGenericReturnType(
            MethodBuilder interfaceMethod,
            ILGenerator interfaceMethodIL,
            LocalBuilder returnLocal,
            Type concreteGenericType,
            bool takeOwnership,
            bool isNew)
        {
            Type constructedGenericType = concreteGenericType.MakeGenericType(interfaceMethod.GetGenericArguments()[0]);
            ConstructorInfo ctor = constructedGenericType.GetGenericTypeDefinition().GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(IntPtr), typeof(bool), typeof(bool) }, null);
            ctor = TypeBuilder.GetConstructor(constructedGenericType, ctor);

            interfaceMethodIL.Emit(OpCodes.Ldloc_S, returnLocal);
            interfaceMethodIL.Emit(takeOwnership ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            interfaceMethodIL.Emit(isNew ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            interfaceMethodIL.Emit(OpCodes.Newobj, ctor);
        }

        //create a TStackable IStackable
        private static void CreateConcreteStackableReturnType(
            MethodInfo interfaceMethod,
            ILGenerator interfaceMethodIL,
            LocalBuilder returnLocal,
            Type concreteGenericType, //should always be SafeStackHandle<>
            bool takeOwnership)
        {
            MethodInfo creationMethod = concreteGenericType.GetGenericTypeDefinition().GetMethod("CreateSafeBaseHandle", BindingFlags.NonPublic | BindingFlags.Static, null, new Type[] { typeof(IntPtr), typeof(bool) }, null);
            Type constructedGenericType = concreteGenericType.GetGenericTypeDefinition().MakeGenericType(interfaceMethod.GetGenericArguments()[0]);
            MethodInfo genericCreationMethod = TypeBuilder.GetMethod(constructedGenericType, creationMethod);

            interfaceMethodIL.Emit(OpCodes.Ldloc_S, returnLocal);
            interfaceMethodIL.Emit(takeOwnership ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            interfaceMethodIL.Emit(OpCodes.Call, genericCreationMethod);
        }

        //create an object from a generic (SafeStackHandle<>) passed as IntPtr to native method
        private static void CreateConcreteGenericParameterType(
            MethodBuilder interfaceMethod,
            ILGenerator interfaceMethodIL,
            int parameterPosition,
            Type concreteGenericType,
            LocalBuilder[] nativeLocals,
            LocalBuilder[] constructedLocals)
        {
            ConstructorInfo ctor;
            if (concreteGenericType.ContainsGenericParameters)
            {
                Type constructedGenericType = concreteGenericType.MakeGenericType(interfaceMethod.GetGenericArguments()[0]);
                ctor = constructedGenericType.GetGenericTypeDefinition().GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(IntPtr), typeof(bool), typeof(bool) }, null);
                ctor = TypeBuilder.GetConstructor(constructedGenericType, ctor);
            }
            else
                ctor = concreteGenericType.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(IntPtr), typeof(bool), typeof(bool) }, null);

            interfaceMethodIL.Emit(OpCodes.Ldloc_S, nativeLocals[parameterPosition]);
            interfaceMethodIL.Emit(concreteGenericType.Name.EndsWith("own") ? OpCodes.Ldc_I4_0 : OpCodes.Ldc_I4_1);
            interfaceMethodIL.Emit(concreteGenericType.Name.EndsWith("new") ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            interfaceMethodIL.Emit(OpCodes.Newobj, ctor);
            interfaceMethodIL.Emit(OpCodes.Stloc_S, constructedLocals[parameterPosition]);
        }

        #endregion


        #region Pre ByRef native implementation generator
        //private static void CreateInterfaceImplementation(string dllName, TypeBuilder typeBuilder, MethodInfo ifMethod)
        //{
        //    CustomAttributeBuilder attrBuilder;
        //    Type attrType;
        //    Type[] ctorParams;
        //    ConstructorInfo ctor;
        //    MethodBuilder nativeMethod, interfaceMethod;
        //    ILGenerator interfaceMethodIL;
        //    Type[][] requiredModifiers;
        //    Type concreteType;
        //    MethodInfo postConstruction, checkIntegerReturn, checkSafeHandleReturn;

        //    //get interface method info
        //    string methodName = ifMethod.Name;

        //    Type interfaceReturnType = ifMethod.ReturnType;
        //    Type concreteReturnType = CreateConcreteType(ifMethod.ReturnParameter, out concreteType) ? concreteType : interfaceReturnType;

        //    ParameterInfo[] parameterInfo = ifMethod.GetParameters();
        //    Type[] interfaceParameterTypes = parameterInfo.Select(x => x.ParameterType).ToArray();

        //    postConstruction = typeof(SafeBaseHandle).GetMethod("PostConstruction", BindingFlags.NonPublic | BindingFlags.Instance);
        //    checkIntegerReturn = typeof(Native).GetMethod("ExpectSuccess", BindingFlags.Public | BindingFlags.Static);
        //    checkSafeHandleReturn = typeof(Native).GetMethod("ExpectNonNull", BindingFlags.Public | BindingFlags.Static);

        //    //declare native method
        //    if (!ifMethod.IsGenericMethod)
        //        nativeMethod = typeBuilder.DefineMethod(string.Format("openssl{0}", methodName),
        //            MethodAttributes.Private | MethodAttributes.PinvokeImpl | MethodAttributes.Static,
        //            concreteReturnType, interfaceParameterTypes);
        //    //should ALWAYS be a SafeStackable<>
        //    else
        //        nativeMethod = GenerateGenericNativeMethod(typeBuilder, methodName, concreteReturnType, interfaceParameterTypes);

        //    //declare native method DllImportAttribute
        //    attrType = typeof(DllImportAttribute);
        //    ctorParams = new Type[] { typeof(string) };
        //    ctor = attrType.GetTypeInfo().GetConstructor(ctorParams);
        //    attrBuilder = new CustomAttributeBuilder(ctor, new object[] { dllName },
        //        new FieldInfo[] { attrType.GetTypeInfo().GetField("EntryPoint") },
        //        new object[] { methodName });
        //    nativeMethod.SetCustomAttribute(attrBuilder);

        //    //from C# 7.2: add required attributes (in IL .param followed by .custom)
        //    requiredModifiers = parameterInfo.Select(x => x.GetRequiredCustomModifiers()).ToArray();

        //    //declare interface method
        //    interfaceMethod = typeBuilder.DefineMethod(methodName, MethodAttributes.Public | MethodAttributes.Virtual, CallingConventions.Standard,
        //        ifMethod.ReturnType, null, null, interfaceParameterTypes, requiredModifiers, null);

        //    if (ifMethod.IsGenericMethod)
        //    {
        //        CreateMethodGenericParameter(interfaceMethod, ifMethod, out GenericTypeParameterBuilder[] genericParameters);
        //        if (ifMethod.ReturnType.IsGenericType)
        //        {
        //            //replace return type with correct generic parameterized type
        //            interfaceMethod.SetReturnType(ifMethod.ReturnType.GetGenericTypeDefinition().MakeGenericType(genericParameters[0]));
        //        }
        //    }

        //    interfaceMethodIL = interfaceMethod.GetILGenerator();

        //    //if native method has a return type, define a local and a label
        //    if (interfaceReturnType != typeof(void))
        //    {
        //        if (concreteReturnType.IsGenericType)
        //            interfaceMethodIL.DeclareLocal(concreteReturnType.MakeGenericType(interfaceMethod.GetGenericArguments()[0])); //this is loc_0
        //        else
        //            interfaceMethodIL.DeclareLocal(concreteReturnType); //this is loc_0
        //    }

        //    if (!ifMethod.IsGenericMethod)
        //    {
        //        //load arguments to pass to native method or create a reference
        //        for (int i = 0; i < interfaceParameterTypes.Length; i++)
        //            EmitLoadArgument(i, interfaceMethodIL);

        //        //execute native method
        //        interfaceMethodIL.Emit(OpCodes.Call, nativeMethod);

        //        //store return value
        //        if (interfaceReturnType != typeof(void))
        //            interfaceMethodIL.Emit(OpCodes.Stloc_0);
        //    }
        //    //returns a SafeStackHandle<>
        //    else if (interfaceReturnType.IsGenericType)
        //    {
        //        CreateConcreteGenericReturnType(
        //            interfaceMethod,
        //            interfaceMethodIL,
        //            nativeMethod,
        //            concreteReturnType,
        //            concreteReturnType.Name.EndsWith("own") ? false : true,
        //            concreteReturnType.Name.EndsWith("new") ? true : false);
        //    }
        //    //contains generic parameters (should be SafeStackHandle<>)
        //    //can return an IStackable
        //    else
        //    {
        //        ParameterInfo genericParameter = parameterInfo.Single(x => x.ParameterType.IsGenericType); //is this always 1, get position
        //        bool takeOwnership = genericParameter.GetCustomAttribute<DontTakeOwnershipAttribute>() == null;

        //        CreateConcreteStackableType(
        //            interfaceMethod,
        //            interfaceMethodIL,
        //            nativeMethod,
        //            typeof(SafeStackHandle<>),
        //            interfaceParameterTypes,
        //            takeOwnership);
        //    }

        //    //load return value
        //    if (interfaceReturnType != typeof(void))
        //    {
        //        interfaceMethodIL.Emit(OpCodes.Ldloc_0);

        //        if (ifMethod.GetCustomAttribute<DontCheckReturnTypeAttribute>() is null)
        //        {
        //            //check return values and throw exception if necessary
        //            if (interfaceReturnType == typeof(int)) //TODO: uint/ulong/long
        //            {
        //                interfaceMethodIL.Emit(OpCodes.Call, checkIntegerReturn);
        //                interfaceMethodIL.Emit(OpCodes.Ldloc_0);
        //            }
        //            else if (IsSafeHandle(interfaceReturnType))
        //            {
        //                interfaceMethodIL.Emit(OpCodes.Call, checkSafeHandleReturn);
        //                interfaceMethodIL.Emit(OpCodes.Ldloc_0);
        //            }
        //        }

        //        //always consider abstracts as the (in)correct implementation
        //        if (!concreteReturnType.Equals(interfaceReturnType))
        //        {
        //            interfaceMethodIL.Emit(OpCodes.Callvirt, postConstruction);
        //            interfaceMethodIL.Emit(OpCodes.Ldloc_0);
        //        }
        //    }

        //    interfaceMethodIL.Emit(OpCodes.Ret);

        //    //define interface implementation as the interface override
        //    typeBuilder.DefineMethodOverride(interfaceMethod, ifMethod);
        //}

        //private static MethodBuilder GenerateGenericNativeMethod(
        //    TypeBuilder typeBuilder,
        //    string methodName,
        //    Type concreteReturnType,
        //    Type[] interfaceParameterTypes)
        //{
        //    Type nativeReturnType;
        //    Type[] nativeParamterTypes = new Type[interfaceParameterTypes.Length];

        //    if (concreteReturnType.IsGenericType || concreteReturnType.IsGenericParameter)
        //        nativeReturnType = typeof(IntPtr);
        //    else
        //        nativeReturnType = concreteReturnType;

        //    for (int i = 0; i < interfaceParameterTypes.Length; i++)
        //    {
        //        if (interfaceParameterTypes[i].IsGenericType || interfaceParameterTypes[i].IsGenericParameter)
        //            nativeParamterTypes[i] = typeof(IntPtr);
        //        else
        //            nativeParamterTypes[i] = interfaceParameterTypes[i];
        //    }

        //    return typeBuilder.DefineMethod(string.Format("openssl{0}", methodName),
        //        MethodAttributes.Private | MethodAttributes.PinvokeImpl | MethodAttributes.Static,
        //        nativeReturnType, nativeParamterTypes);
        //}

        //private static void CreateConcreteGenericReturnType(
        //    MethodBuilder interfaceMethod,
        //    ILGenerator interfaceMethodIL,
        //    MethodInfo nativeMethod,
        //    Type concreteGenericType,
        //    bool takeOwnership,
        //    bool isNew)
        //{
        //    LocalBuilder ptr;
        //    Type constructedGenericType = concreteGenericType.MakeGenericType(interfaceMethod.GetGenericArguments()[0]);
        //    ConstructorInfo ctor = constructedGenericType.GetGenericTypeDefinition().GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(IntPtr), typeof(bool), typeof(bool) }, null);
        //    ctor = TypeBuilder.GetConstructor(constructedGenericType, ctor);

        //    ptr = interfaceMethodIL.DeclareLocal(typeof(IntPtr));

        //    interfaceMethodIL.Emit(OpCodes.Call, nativeMethod);
        //    interfaceMethodIL.Emit(OpCodes.Stloc_S, ptr);
        //    interfaceMethodIL.Emit(OpCodes.Ldloc_S, ptr);
        //    interfaceMethodIL.Emit(takeOwnership ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        //    interfaceMethodIL.Emit(isNew ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        //    interfaceMethodIL.Emit(OpCodes.Newobj, ctor);
        //    interfaceMethodIL.Emit(OpCodes.Stloc_0);
        //}

        //private static void CreateConcreteStackableType(
        //    MethodInfo interfaceMethod,
        //    ILGenerator interfaceMethodIL,
        //    MethodBuilder nativeMethod,
        //    Type genericType,
        //    Type[] interfaceParameterTypes,
        //    bool takeOwnership)
        //{
        //    LocalBuilder stackPtr, returnPtr;
        //    MethodInfo creationMethod = genericType.GetMethod("CreateSafeBaseHandle", BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(IntPtr), typeof(bool) }, null);
        //    MethodInfo getHandleMethod = typeof(SafeHandle).GetMethod("DangerousGetHandle");

        //    Type constructedGenericType = genericType.MakeGenericType(interfaceMethod.GetGenericArguments()[0]);
        //    MethodInfo genericCreationMethod = TypeBuilder.GetMethod(constructedGenericType, creationMethod);

        //    stackPtr = interfaceMethodIL.DeclareLocal(typeof(IntPtr));
        //    returnPtr = interfaceMethodIL.DeclareLocal(typeof(IntPtr));

        //    interfaceMethodIL.Emit(OpCodes.Ldarg_1); //is this always 1? add a verification
        //    interfaceMethodIL.Emit(OpCodes.Call, getHandleMethod);
        //    interfaceMethodIL.Emit(OpCodes.Stloc_S, stackPtr);
        //    //load all parameters to pass to the native method
        //    interfaceMethodIL.Emit(OpCodes.Ldloc_S, stackPtr); //should always be the first parameter
        //    for (int i = 1; i < interfaceParameterTypes.Length; i++)
        //        EmitLoadArgument(i, interfaceMethodIL);
        //    interfaceMethodIL.Emit(OpCodes.Call, nativeMethod);

        //    //store return value
        //    if (nativeMethod.ReturnType == typeof(IntPtr))
        //    {
        //        interfaceMethodIL.Emit(OpCodes.Stloc_S, returnPtr);
        //        interfaceMethodIL.Emit(takeOwnership ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        //        interfaceMethodIL.Emit(OpCodes.Call, genericCreationMethod);
        //        interfaceMethodIL.Emit(OpCodes.Stloc_0);
        //    }
        //    else if (nativeMethod.ReturnType != typeof(void))
        //        interfaceMethodIL.Emit(OpCodes.Stloc_0);
        //}

        //private static bool IsBaseClass(Type type)
        //{
        //    if (!type.IsAbstract)
        //        return false;

        //    Type currentType = type;
        //    while (currentType.BaseType != null)
        //    {
        //        currentType = currentType.BaseType;
        //        if (currentType.Equals(typeof(SafeBaseHandle)))
        //            return true;
        //    }

        //    return false;
        //}

        //private static bool IsSafeHandle(Type type)
        //{
        //    Type currentType = type;
        //    while (currentType.BaseType != null)
        //    {
        //        currentType = currentType.BaseType;
        //        if (currentType.Equals(typeof(SafeHandle)))
        //            return true;
        //    }

        //    return false;
        //}

        //private static bool CreateConcreteType(ParameterInfo parameter, out Type concreteType)
        //{
        //    concreteType = parameter.ParameterType;
        //    NewSafeHandleAttribute newAttr;
        //    DontTakeOwnershipAttribute ownAttr;
        //    TypeBuilder typeBuilderNew, typeBuilderOwn, typeBuilderRef;
        //    ConstructorInfo baseCtor, ptrCtor = null;
        //    ConstructorBuilder ctorBuilder;
        //    ILGenerator ctorIL;
        //    Type parameterType, genericType = null;

        //    parameterType = parameter.ParameterType;
        //    if (!IsBaseClass(parameterType))
        //        return false;

        //    if (!dictConcreteTypes.ContainsKey(parameterType))
        //    {
        //        if (parameterType.IsGenericType)
        //        {
        //            genericType = parameterType.GetGenericTypeDefinition();

        //            if (genericType != typeof(SafeStackHandle<>))
        //                throw new InvalidOperationException("Unknown generic type");

        //            baseCtor = genericType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(bool), typeof(bool) }, null);
        //            ptrCtor = genericType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(IntPtr), typeof(bool), typeof(bool) }, null);
        //        }
        //        else
        //            baseCtor = parameterType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(bool), typeof(bool) }, null);

        //        typeBuilderNew = moduleBuilder.DefineType($"{parameterType.Name}_new", TypeAttributes.NotPublic, genericType ?? parameterType);

        //        if (!(genericType is null))
        //            CreateClassGenericParameter(typeBuilderNew, genericType);

        //        //default constructor
        //        ctorBuilder = typeBuilderNew.DefineConstructor(MethodAttributes.Private, CallingConventions.Standard, null);
        //        ctorIL = ctorBuilder.GetILGenerator();
        //        ctorIL.Emit(OpCodes.Ldarg_0);
        //        ctorIL.Emit(OpCodes.Ldc_I4_1);
        //        ctorIL.Emit(OpCodes.Ldc_I4_1);
        //        ctorIL.Emit(OpCodes.Call, baseCtor);
        //        ctorIL.Emit(OpCodes.Ret);

        //        if (!(ptrCtor is null))
        //            CreatePtrConstructor(typeBuilderNew, ptrCtor);

        //        typeBuilderOwn = moduleBuilder.DefineType($"{parameterType.Name}_own", TypeAttributes.NotPublic, genericType ?? parameterType);

        //        if (!(genericType is null))
        //            CreateClassGenericParameter(typeBuilderOwn, genericType);

        //        ctorBuilder = typeBuilderOwn.DefineConstructor(MethodAttributes.Private, CallingConventions.Standard, null);
        //        ctorIL = ctorBuilder.GetILGenerator();
        //        ctorIL.Emit(OpCodes.Ldarg_0);
        //        ctorIL.Emit(OpCodes.Ldc_I4_0);
        //        ctorIL.Emit(OpCodes.Ldc_I4_0);
        //        ctorIL.Emit(OpCodes.Call, baseCtor);
        //        ctorIL.Emit(OpCodes.Ret);

        //        if (!(ptrCtor is null))
        //            CreatePtrConstructor(typeBuilderOwn, ptrCtor);

        //        typeBuilderRef = moduleBuilder.DefineType($"{parameterType.Name}_ref", TypeAttributes.NotPublic, genericType ?? parameterType);

        //        if (!(genericType is null))
        //            CreateClassGenericParameter(typeBuilderRef, genericType);

        //        ctorBuilder = typeBuilderRef.DefineConstructor(MethodAttributes.Private, CallingConventions.Standard, null);
        //        ctorIL = ctorBuilder.GetILGenerator();
        //        ctorIL.Emit(OpCodes.Ldarg_0);
        //        ctorIL.Emit(OpCodes.Ldc_I4_1);
        //        ctorIL.Emit(OpCodes.Ldc_I4_0);
        //        ctorIL.Emit(OpCodes.Call, baseCtor);
        //        ctorIL.Emit(OpCodes.Ret);

        //        if (!(ptrCtor is null))
        //            CreatePtrConstructor(typeBuilderRef, ptrCtor);

        //        dictConcreteTypes.Add(parameterType, Tuple.Create<Type, Type, Type>
        //            (
        //                typeBuilderNew.CreateTypeInfo().AsType(),
        //                typeBuilderOwn.CreateTypeInfo().AsType(),
        //                typeBuilderRef.CreateTypeInfo().AsType()
        //            ));
        //    }

        //    if (!((newAttr = parameter.GetCustomAttribute<NewSafeHandleAttribute>()) is null))
        //        concreteType = dictConcreteTypes[parameterType].Item1;
        //    else if (!((ownAttr = parameter.GetCustomAttribute<DontTakeOwnershipAttribute>()) is null))
        //        concreteType = dictConcreteTypes[parameterType].Item2;
        //    else
        //        concreteType = dictConcreteTypes[parameterType].Item3;

        //    return true;
        //}

        ////load arguments (as instance you need to skip Ldarg_0)
        //private static void EmitLoadArgument(int argumentPosition, ILGenerator ilGen)
        //{
        //    switch (argumentPosition)
        //    {
        //        case 0:
        //            ilGen.Emit(OpCodes.Ldarg_1);
        //            break;
        //        case 1:
        //            ilGen.Emit(OpCodes.Ldarg_2);
        //            break;
        //        case 2:
        //            ilGen.Emit(OpCodes.Ldarg_3);
        //            break;
        //        default:
        //            ilGen.Emit(OpCodes.Ldarg_S, (argumentPosition + 1));
        //            break;
        //    }
        //}

        //private static void CreateClassGenericParameter(TypeBuilder typeBuilder, Type genericTypeDefenition)
        //{
        //    Type[] genericArguments = genericTypeDefenition.GetGenericArguments();
        //    GenericTypeParameterBuilder[] genericParameters = typeBuilder.DefineGenericParameters(genericArguments.Select(x => x.Name).ToArray());

        //    for (int i = 0; i < genericArguments.Length; i++)
        //    {
        //        foreach (Type t in genericArguments[i].GetGenericParameterConstraints())
        //        {
        //            if (t.IsInterface)
        //                genericParameters[i].SetInterfaceConstraints(t);
        //            else if (t.IsAbstract)
        //                genericParameters[i].SetBaseTypeConstraint(t);
        //            else
        //                throw new InvalidOperationException("Invalid generic constraint type");
        //        }
        //    }
        //}

        //private static void CreateMethodGenericParameter(MethodBuilder methodBuilder, MethodInfo method, out GenericTypeParameterBuilder[] genericParameters)
        //{
        //    Type[] genericArguments = method.GetGenericArguments();
        //    genericParameters = methodBuilder.DefineGenericParameters(genericArguments.Select(x => string.Concat(x.Name, "_new")).ToArray());

        //    for (int i = 0; i < genericArguments.Length; i++)
        //    {
        //        foreach (Type t in genericArguments[i].GetGenericParameterConstraints())
        //        {
        //            if (t.IsInterface)
        //                genericParameters[i].SetInterfaceConstraints(t);
        //            else if (t.IsAbstract)
        //                genericParameters[i].SetBaseTypeConstraint(t);
        //            else
        //                throw new InvalidOperationException("Invalid generic constraint type");
        //        }
        //    }
        //}

        //private static void CreatePtrConstructor(TypeBuilder typeBuilder, ConstructorInfo ptrCtor)
        //{
        //    ConstructorBuilder ctorBuilder = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, new Type[] { typeof(IntPtr), typeof(bool), typeof(bool) });
        //    ILGenerator ctorIL = ctorBuilder.GetILGenerator();
        //    ctorIL.Emit(OpCodes.Ldarg_0);
        //    ctorIL.Emit(OpCodes.Ldarg_1);
        //    ctorIL.Emit(OpCodes.Ldarg_2);
        //    ctorIL.Emit(OpCodes.Ldarg_3);
        //    ctorIL.Emit(OpCodes.Call, ptrCtor);
        //    ctorIL.Emit(OpCodes.Ret);
        //}
        #endregion
    }
}