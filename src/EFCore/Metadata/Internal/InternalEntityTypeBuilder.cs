// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Internal;
using Microsoft.EntityFrameworkCore.Utilities;
using Microsoft.EntityFrameworkCore.ValueGeneration.Internal;

namespace Microsoft.EntityFrameworkCore.Metadata.Internal
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public class InternalEntityTypeBuilder : AnnotatableBuilder<EntityType, InternalModelBuilder>, IConventionEntityTypeBuilder
    {
        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public InternalEntityTypeBuilder([NotNull] EntityType metadata, [NotNull] InternalModelBuilder modelBuilder)
            : base(metadata, modelBuilder)
        {
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalKeyBuilder PrimaryKey(
            [CanBeNull] IReadOnlyList<string> propertyNames, ConfigurationSource configurationSource)
            => PrimaryKey(GetOrCreateProperties(propertyNames, configurationSource, required: true), configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalKeyBuilder PrimaryKey(
            [CanBeNull] IReadOnlyList<MemberInfo> clrMembers, ConfigurationSource configurationSource)
            => PrimaryKey(GetOrCreateProperties(clrMembers, configurationSource), configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalKeyBuilder PrimaryKey(
            [CanBeNull] IReadOnlyList<Property> properties, ConfigurationSource configurationSource)
        {
            if (!CanSetPrimaryKey(properties, configurationSource))
            {
                return null;
            }

            InternalKeyBuilder keyBuilder = null;
            if (properties == null)
            {
                Metadata.SetPrimaryKey(properties, configurationSource);
            }
            else
            {
                var previousPrimaryKey = Metadata.FindPrimaryKey();
                if (previousPrimaryKey != null
                    && PropertyListComparer.Instance.Compare(previousPrimaryKey.Properties, properties) == 0)
                {
                    previousPrimaryKey.UpdateConfigurationSource(configurationSource);
                    return Metadata.SetPrimaryKey(properties, configurationSource).Builder;
                }

                using (ModelBuilder.Metadata.ConventionDispatcher.DelayConventions())
                {
                    keyBuilder = HasKeyInternal(properties, configurationSource);
                    if (keyBuilder == null)
                    {
                        return null;
                    }

                    Metadata.SetPrimaryKey(keyBuilder.Metadata.Properties, configurationSource);
                    foreach (var key in Metadata.GetDeclaredKeys().ToList())
                    {
                        if (key == keyBuilder.Metadata)
                        {
                            continue;
                        }

                        var referencingForeignKeys = key
                            .GetReferencingForeignKeys()
                            .Where(fk => fk.GetPrincipalKeyConfigurationSource() == null)
                            .ToList();

                        foreach (var referencingForeignKey in referencingForeignKeys)
                        {
                            DetachRelationship(referencingForeignKey).Attach();
                        }
                    }

                    if (previousPrimaryKey?.Builder != null)
                    {
                        RemoveKeyIfUnused(previousPrimaryKey, configurationSource);
                    }
                }
            }

            // TODO: Use convention batch to get the updated builder, see #15898
            if (keyBuilder?.Metadata.Builder == null)
            {
                properties = GetActualProperties(properties, null);
                return properties == null ? null : Metadata.FindPrimaryKey(properties).Builder;
            }

            return keyBuilder;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool CanSetPrimaryKey([CanBeNull] IReadOnlyList<IConventionProperty> properties, ConfigurationSource configurationSource)
        {
            var previousPrimaryKey = Metadata.FindPrimaryKey();
            if (properties == null)
            {
                if (previousPrimaryKey == null)
                {
                    return true;
                }
            }
            else if (previousPrimaryKey != null
                && PropertyListComparer.Instance.Compare(previousPrimaryKey.Properties, properties) == 0)
            {
                return true;
            }

            return configurationSource.Overrides(Metadata.GetPrimaryKeyConfigurationSource())
                && (properties == null
                    || !Metadata.IsKeyless
                    || configurationSource.Overrides(Metadata.GetIsKeylessConfigurationSource()));
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalKeyBuilder HasKey([NotNull] IReadOnlyList<string> propertyNames, ConfigurationSource configurationSource)
            => HasKeyInternal(GetOrCreateProperties(propertyNames, configurationSource, required: true), configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalKeyBuilder HasKey([NotNull] IReadOnlyList<MemberInfo> clrMembers, ConfigurationSource configurationSource)
            => HasKeyInternal(GetOrCreateProperties(clrMembers, configurationSource), configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalKeyBuilder HasKey([NotNull] IReadOnlyList<Property> properties, ConfigurationSource? configurationSource)
            => HasKeyInternal(properties, configurationSource);

        private InternalKeyBuilder HasKeyInternal(IReadOnlyList<Property> properties, ConfigurationSource? configurationSource)
        {
            if (properties == null)
            {
                return null;
            }

            var actualProperties = GetActualProperties(properties, configurationSource);
            var key = Metadata.FindDeclaredKey(actualProperties);
            if (key == null)
            {
                if (configurationSource == null)
                {
                    return null;
                }

                if (Metadata.IsKeyless
                    && !configurationSource.Overrides(Metadata.GetIsKeylessConfigurationSource()))
                {
                    return null;
                }

                if (Metadata.GetIsKeylessConfigurationSource() != ConfigurationSource.Explicit)
                {
                    Metadata.SetIsKeyless(false, configurationSource.Value);
                }

                var containingForeignKeys = actualProperties
                    .SelectMany(p => p.GetContainingForeignKeys().Where(k => k.DeclaringEntityType != Metadata))
                    .ToList();

                if (containingForeignKeys.Any(fk => !configurationSource.Overrides(fk.GetPropertiesConfigurationSource())))
                {
                    return null;
                }

                if (configurationSource != ConfigurationSource.Explicit // let it throw for explicit
                    && actualProperties.Any(p => !p.Builder.CanSetIsRequired(true, configurationSource)))
                {
                    return null;
                }

                using (Metadata.Model.ConventionDispatcher.DelayConventions())
                {
                    foreach (var foreignKey in containingForeignKeys)
                    {
                        if (foreignKey.GetPropertiesConfigurationSource() == ConfigurationSource.Explicit)
                        {
                            // let it throw for explicit
                            continue;
                        }

                        foreignKey.Builder.HasForeignKey((IReadOnlyList<Property>)null, configurationSource.Value);
                    }

                    foreach (var actualProperty in actualProperties)
                    {
                        actualProperty.Builder.IsRequired(true, configurationSource.Value);
                    }

                    key = Metadata.AddKey(actualProperties, configurationSource.Value);
                }

                if (key.Builder == null)
                {
                    key = Metadata.FindDeclaredKey(actualProperties);
                }
            }
            else if (configurationSource.HasValue)
            {
                key.UpdateConfigurationSource(configurationSource.Value);
                Metadata.SetIsKeyless(false, configurationSource.Value);
            }

            return key?.Builder;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalEntityTypeBuilder HasNoKey([NotNull] Key key, ConfigurationSource configurationSource)
        {
            var currentConfigurationSource = key.GetConfigurationSource();
            if (!configurationSource.Overrides(currentConfigurationSource))
            {
                return null;
            }

            using (Metadata.Model.ConventionDispatcher.DelayConventions())
            {
                var detachedRelationships = key.GetReferencingForeignKeys().ToList().Select(DetachRelationship).ToList();

                Metadata.RemoveKey(key);

                foreach (var detachedRelationship in detachedRelationships)
                {
                    detachedRelationship.Attach();
                }

                RemoveUnusedShadowProperties(key.Properties);
                foreach (var property in key.Properties)
                {
                    if (!property.IsKey()
                        && property.ClrType.IsNullableType()
                        && !property.GetContainingForeignKeys().Any(fk => fk.IsRequired))
                    {
                        // TODO: This should be handled by reference tracking, see #15898
                        property.Builder?.IsRequired(null, configurationSource);
                    }
                }
            }

            return this;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool CanRemoveKey([NotNull] Key key, ConfigurationSource configurationSource)
            => configurationSource.Overrides(key.GetConfigurationSource());

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public static List<(InternalKeyBuilder, ConfigurationSource?)> DetachKeys([NotNull] IEnumerable<Key> keysToDetach)
        {
            var keysToDetachList = (keysToDetach as List<Key>) ?? keysToDetach.ToList();
            if (keysToDetachList.Count == 0)
            {
                return null;
            }

            var detachedKeys = new List<(InternalKeyBuilder, ConfigurationSource?)>();
            foreach (var keyToDetach in keysToDetachList)
            {
                var detachedKey = DetachKey(keyToDetach);
                detachedKeys.Add(detachedKey);
            }

            return detachedKeys;
        }

        private static (InternalKeyBuilder, ConfigurationSource?) DetachKey(Key keyToDetach)
        {
            var entityTypeBuilder = keyToDetach.DeclaringEntityType.Builder;
            var keyBuilder = keyToDetach.Builder;

            var primaryKeyConfigurationSource = keyToDetach.IsPrimaryKey()
                ? keyToDetach.DeclaringEntityType.GetPrimaryKeyConfigurationSource()
                : null;

            if (entityTypeBuilder == null)
            {
                keyToDetach.DeclaringEntityType.RemoveKey(keyToDetach.Properties);
            }
            else
            {
                entityTypeBuilder.HasNoKey(keyToDetach, keyToDetach.GetConfigurationSource());
            }

            return (keyBuilder, primaryKeyConfigurationSource);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalEntityTypeBuilder HasNoKey(ConfigurationSource configurationSource)
        {
            if (Metadata.IsKeyless)
            {
                Metadata.SetIsKeyless(true, configurationSource);
                return this;
            }

            if (!CanRemoveKey(configurationSource))
            {
                return null;
            }

            using (Metadata.Model.ConventionDispatcher.DelayConventions())
            {
                foreach (var foreignKey in Metadata.GetReferencingForeignKeys().ToList())
                {
                    foreignKey.DeclaringEntityType.Builder.HasNoRelationship(foreignKey, configurationSource);
                }

                foreach (var foreignKey in Metadata.GetForeignKeys())
                {
                    foreignKey.HasPrincipalToDependent((string)null, configurationSource);
                }

                foreach (var key in Metadata.GetKeys().ToList())
                {
                    if (key.GetConfigurationSource() != ConfigurationSource.Explicit)
                    {
                        HasNoKey(key, configurationSource);
                    }
                }

                Metadata.SetIsKeyless(true, configurationSource);
                return this;
            }
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool CanRemoveKey(ConfigurationSource configurationSource)
            => Metadata.IsKeyless
                || (configurationSource.Overrides(Metadata.GetIsKeylessConfigurationSource())
                    && !Metadata.GetKeys().Any(key => !configurationSource.Overrides(key.GetConfigurationSource()))
                    && !Metadata.GetReferencingForeignKeys().Any(fk => !configurationSource.Overrides(fk.GetConfigurationSource()))
                    && !Metadata.GetForeignKeys().Any(fk => !configurationSource.Overrides(fk.GetPrincipalToDependentConfigurationSource())));

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalPropertyBuilder Property(
            [CanBeNull] Type propertyType,
            [NotNull] string propertyName,
            ConfigurationSource? configurationSource)
            => Property(propertyType, propertyName, typeConfigurationSource: configurationSource, configurationSource: configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalPropertyBuilder Property(
            [CanBeNull] Type propertyType,
            [NotNull] string propertyName,
            ConfigurationSource? typeConfigurationSource,
            ConfigurationSource? configurationSource)
            => Property(
                propertyType, propertyName, memberInfo: null,
                typeConfigurationSource: typeConfigurationSource,
                configurationSource: configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalPropertyBuilder Property([NotNull] string propertyName, ConfigurationSource? configurationSource)
            => Property(propertyType: null, propertyName, memberInfo: null, typeConfigurationSource: null, configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalPropertyBuilder Property([NotNull] MemberInfo memberInfo, ConfigurationSource? configurationSource)
            => Property(memberInfo.GetMemberType(), memberInfo.GetSimpleMemberName(), memberInfo, configurationSource, configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalPropertyBuilder IndexerProperty(
            [CanBeNull] Type propertyType,
            [NotNull] string propertyName,
            ConfigurationSource? configurationSource)
        {
            var indexerPropertyInfo = Metadata.FindIndexerPropertyInfo();
            if (indexerPropertyInfo == null)
            {
                throw new InvalidOperationException(
                    CoreStrings.NonIndexerEntityType(propertyName, Metadata.DisplayName(), typeof(string).ShortDisplayName()));
            }

            return Property(propertyType, propertyName, indexerPropertyInfo, configurationSource, configurationSource);
        }

        private InternalPropertyBuilder Property(
            [CanBeNull] Type propertyType,
            [NotNull] string propertyName,
            [CanBeNull] MemberInfo memberInfo,
            ConfigurationSource? typeConfigurationSource,
            ConfigurationSource? configurationSource)
        {
            var entityType = Metadata;
            List<Property> propertiesToDetach = null;
            var existingProperty = entityType.FindProperty(propertyName);
            if (existingProperty != null)
            {
                if (existingProperty.DeclaringEntityType != Metadata)
                {
                    if (!IsIgnored(propertyName, configurationSource))
                    {
                        Metadata.RemoveIgnored(propertyName);
                    }

                    entityType = existingProperty.DeclaringEntityType;
                }

                var existingMember = existingProperty.GetIdentifyingMemberInfo();
                if ((memberInfo == null || existingMember.IsOverridenBy(memberInfo))
                    && (propertyType == null || propertyType == existingProperty.ClrType))
                {
                    if (configurationSource.HasValue)
                    {
                        existingProperty.UpdateConfigurationSource(configurationSource.Value);
                    }

                    if (propertyType != null
                        && typeConfigurationSource.HasValue)
                    {
                        existingProperty.UpdateTypeConfigurationSource(typeConfigurationSource.Value);
                    }

                    return existingProperty.Builder;
                }

                if (!configurationSource.Overrides(existingProperty.GetConfigurationSource()))
                {
                    return null;
                }

                if (propertyType == null)
                {
                    propertyType = existingProperty.ClrType;
                }

                propertiesToDetach = new List<Property> { existingProperty };
            }
            else
            {
                if (!configurationSource.HasValue
                    || IsIgnored(propertyName, configurationSource))
                {
                    return null;
                }

                foreach (var conflictingServiceProperty in Metadata.FindServicePropertiesInHierarchy(propertyName))
                {
                    if (!configurationSource.Overrides(conflictingServiceProperty.GetConfigurationSource()))
                    {
                        return null;
                    }
                }

                foreach (var conflictingNavigation in Metadata.FindNavigationsInHierarchy(propertyName))
                {
                    var foreignKey = conflictingNavigation.ForeignKey;

                    var navigationConfigurationSource = conflictingNavigation.GetConfigurationSource();
                    if (!configurationSource.Overrides(navigationConfigurationSource))
                    {
                        return null;
                    }

                    if (navigationConfigurationSource == ConfigurationSource.Explicit)
                    {
                        throw new InvalidOperationException(
                            CoreStrings.PropertyCalledOnNavigation(propertyName, Metadata.DisplayName()));
                    }
                }

                foreach (var conflictingSkipNavigation in Metadata.FindSkipNavigationsInHierarchy(propertyName))
                {
                    if (!configurationSource.Overrides(conflictingSkipNavigation.GetConfigurationSource()))
                    {
                        return null;
                    }
                }

                if (memberInfo == null)
                {
                    memberInfo = Metadata.ClrType?.GetMembersInHierarchy(propertyName).FirstOrDefault();
                }

                if (propertyType == null)
                {
                    if (memberInfo == null)
                    {
                        throw new InvalidOperationException(CoreStrings.NoPropertyType(propertyName, Metadata.DisplayName()));
                    }

                    propertyType = memberInfo.GetMemberType();
                    typeConfigurationSource = ConfigurationSource.Explicit;
                }
                else if (memberInfo != null
                      && propertyType != memberInfo.GetMemberType()
                      && memberInfo != Metadata.FindIndexerPropertyInfo()
                      && typeConfigurationSource != null)
                {
                    return null;
                }

                foreach (var derivedType in Metadata.GetDerivedTypes())
                {
                    var derivedProperty = derivedType.FindDeclaredProperty(propertyName);
                    if (derivedProperty != null)
                    {
                        if (propertiesToDetach == null)
                        {
                            propertiesToDetach = new List<Property>();
                        }

                        propertiesToDetach.Add(derivedProperty);
                    }
                }
            }

            InternalPropertyBuilder builder;
            using (Metadata.Model.ConventionDispatcher.DelayConventions())
            {
                var detachedProperties = propertiesToDetach == null ? null : DetachProperties(propertiesToDetach);

                if (existingProperty == null)
                {
                    Metadata.RemoveIgnored(propertyName);

                    foreach (var conflictingServiceProperty in Metadata.FindServicePropertiesInHierarchy(propertyName))
                    {
                        if (conflictingServiceProperty.GetConfigurationSource() != ConfigurationSource.Explicit)
                        {
                            conflictingServiceProperty.DeclaringEntityType.RemoveServiceProperty(conflictingServiceProperty);
                        }
                    }

                    foreach (var conflictingNavigation in Metadata.FindNavigationsInHierarchy(propertyName))
                    {
                        if (conflictingNavigation.GetConfigurationSource() == ConfigurationSource.Explicit)
                        {
                            continue;
                        }

                        var foreignKey = conflictingNavigation.ForeignKey;
                        if (foreignKey.GetConfigurationSource() == ConfigurationSource.Convention)
                        {
                            foreignKey.DeclaringEntityType.Builder.HasNoRelationship(foreignKey, ConfigurationSource.Convention);
                        }
                        else if (foreignKey.Builder.HasNavigation(
                                (string)null,
                                conflictingNavigation.IsOnDependent,
                                configurationSource.Value) == null)
                        {
                            return null;
                        }
                    }

                    foreach (var conflictingSkipNavigation in Metadata.FindSkipNavigationsInHierarchy(propertyName))
                    {
                        if (conflictingSkipNavigation.GetConfigurationSource() == ConfigurationSource.Explicit)
                        {
                            continue;
                        }

                        var inverse = conflictingSkipNavigation.Inverse;
                        if (inverse?.Builder != null
                            && inverse.DeclaringEntityType.Builder
                                .CanRemoveSkipNavigation(inverse, configurationSource))
                        {
                            inverse.DeclaringEntityType.RemoveSkipNavigation(inverse);
                        }

                        conflictingSkipNavigation.DeclaringEntityType.Builder.HasNoSkipNavigation(
                            conflictingSkipNavigation, configurationSource.Value);
                    }
                }

                builder = entityType.AddProperty(
                        propertyName, propertyType, memberInfo, typeConfigurationSource, configurationSource.Value).Builder;

                detachedProperties?.Attach(this);
            }

            return builder.Metadata.Builder == null
                    ? Metadata.FindProperty(propertyName)?.Builder
                    : builder;
        }

        private bool CanRemoveProperty(
            [NotNull] Property property, ConfigurationSource configurationSource, bool canOverrideSameSource = true)
        {
            Check.NotNull(property, nameof(property));
            Check.DebugAssert(property.DeclaringEntityType == Metadata, "property.DeclaringEntityType != Metadata");

            var currentConfigurationSource = property.GetConfigurationSource();
            return configurationSource.Overrides(currentConfigurationSource)
                && (canOverrideSameSource || (configurationSource != currentConfigurationSource));
        }

        private ConfigurationSource? RemoveProperty(
            Property property, ConfigurationSource configurationSource, bool canOverrideSameSource = true)
        {
            var currentConfigurationSource = property.GetConfigurationSource();
            if (!configurationSource.Overrides(currentConfigurationSource)
                || !(canOverrideSameSource || (configurationSource != currentConfigurationSource)))
            {
                return null;
            }

            using (Metadata.Model.ConventionDispatcher.DelayConventions())
            {
                var detachedRelationships = property.GetContainingForeignKeys().ToList()
                    .Select(DetachRelationship).ToList();

                foreach (var key in property.GetContainingKeys().ToList())
                {
                    detachedRelationships.AddRange(
                        key.GetReferencingForeignKeys().ToList()
                            .Select(DetachRelationship));
                    var removed = key.DeclaringEntityType.Builder.HasNoKey(key, configurationSource);
                    Check.DebugAssert(removed != null, "removed is null");
                }

                foreach (var index in property.GetContainingIndexes().ToList())
                {
                    var removed = index.DeclaringEntityType.Builder.HasNoIndex(index, configurationSource);
                    Check.DebugAssert(removed != null, "removed is null");
                }

                if (property.Builder != null)
                {
                    var removedProperty = Metadata.RemoveProperty(property.Name);
                    Check.DebugAssert(removedProperty == property, "removedProperty != property");
                }

                foreach (var relationshipSnapshot in detachedRelationships)
                {
                    relationshipSnapshot.Attach();
                }
            }

            return currentConfigurationSource;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual IMutableNavigationBase Navigation([NotNull] MemberInfo memberInfo)
            => Navigation(memberInfo.GetSimpleMemberName());

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual IMutableNavigationBase Navigation([NotNull] string navigationName)
        {
            var existingNavigation = Metadata.FindNavigation(navigationName);
            var existingSkipNavigation = Metadata.FindSkipNavigation(navigationName);
            if (existingNavigation == null
                && existingSkipNavigation == null)
            {
                throw new InvalidOperationException(
                    CoreStrings.CanOnlyConfigureExistingNavigations(navigationName, Metadata.DisplayName()));
            }

            return ((IMutableNavigationBase)existingNavigation) ?? existingSkipNavigation;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalServicePropertyBuilder ServiceProperty(
            [NotNull] MemberInfo memberInfo, ConfigurationSource? configurationSource)
        {
            var propertyName = memberInfo.GetSimpleMemberName();
            List<ServiceProperty> propertiesToDetach = null;
            InternalServicePropertyBuilder builder = null;
            var existingProperty = Metadata.FindServiceProperty(propertyName);
            if (existingProperty != null)
            {
                if (existingProperty.DeclaringEntityType != Metadata)
                {
                    if (!IsIgnored(propertyName, configurationSource))
                    {
                        Metadata.RemoveIgnored(propertyName);
                    }
                }

                if (existingProperty.GetIdentifyingMemberInfo().IsOverridenBy(memberInfo))
                {
                    if (configurationSource.HasValue)
                    {
                        existingProperty.UpdateConfigurationSource(configurationSource.Value);
                    }

                    return existingProperty.Builder;
                }

                if (!configurationSource.Overrides(existingProperty.GetConfigurationSource()))
                {
                    return null;
                }

                propertiesToDetach = new List<ServiceProperty> { existingProperty };
            }
            else if (!CanAddServiceProperty(memberInfo, configurationSource))
            {
                return null;
            }
            else
            {
                foreach (var derivedType in Metadata.GetDerivedTypes())
                {
                    var derivedProperty = derivedType.FindDeclaredServiceProperty(propertyName);
                    if (derivedProperty != null)
                    {
                        if (propertiesToDetach == null)
                        {
                            propertiesToDetach = new List<ServiceProperty>();
                        }

                        propertiesToDetach.Add(derivedProperty);
                    }
                }
            }

            using (ModelBuilder.Metadata.ConventionDispatcher.DelayConventions())
            {
                List<InternalServicePropertyBuilder> detachedProperties = null;
                if (propertiesToDetach != null)
                {
                    detachedProperties = new List<InternalServicePropertyBuilder>();
                    foreach (var propertyToDetach in propertiesToDetach)
                    {
                        detachedProperties.Add(DetachServiceProperty(propertyToDetach));
                    }
                }

                if (existingProperty == null)
                {
                    Metadata.RemoveIgnored(propertyName);

                    foreach (var conflictingProperty in Metadata.FindPropertiesInHierarchy(propertyName).ToList())
                    {
                        if (conflictingProperty.GetConfigurationSource() != ConfigurationSource.Explicit)
                        {
                            conflictingProperty.DeclaringEntityType.Builder.RemoveProperty(conflictingProperty, configurationSource.Value);
                        }
                    }

                    foreach (var conflictingNavigation in Metadata.FindNavigationsInHierarchy(propertyName).ToList())
                    {
                        if (conflictingNavigation.GetConfigurationSource() == ConfigurationSource.Explicit)
                        {
                            continue;
                        }

                        var foreignKey = conflictingNavigation.ForeignKey;
                        if (foreignKey.GetConfigurationSource() == ConfigurationSource.Convention)
                        {
                            foreignKey.DeclaringEntityType.Builder.HasNoRelationship(foreignKey, ConfigurationSource.Convention);
                        }
                        else if (foreignKey.Builder.HasNavigation(
                                (string)null,
                                conflictingNavigation.IsOnDependent,
                                configurationSource.Value) == null)
                        {
                            return null;
                        }
                    }

                    foreach (var conflictingSkipNavigation in Metadata.FindSkipNavigationsInHierarchy(propertyName).ToList())
                    {
                        if (conflictingSkipNavigation.GetConfigurationSource() == ConfigurationSource.Explicit)
                        {
                            continue;
                        }

                        var inverse = conflictingSkipNavigation.Inverse;
                        if (inverse?.Builder != null
                            && inverse.DeclaringEntityType.Builder
                                .CanRemoveSkipNavigation(inverse, configurationSource))
                        {
                            inverse.DeclaringEntityType.RemoveSkipNavigation(inverse);
                        }

                        conflictingSkipNavigation.DeclaringEntityType.Builder.HasNoSkipNavigation(
                            conflictingSkipNavigation, configurationSource.Value);
                    }
                }

                builder = Metadata.AddServiceProperty(memberInfo, configurationSource.Value).Builder;

                if (detachedProperties != null)
                {
                    foreach (var detachedProperty in detachedProperties)
                    {
                        detachedProperty.Attach(this);
                    }
                }
            }

            return builder.Metadata.Builder == null
                    ? Metadata.FindServiceProperty(propertyName)?.Builder
                    : builder;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool CanHaveServiceProperty([NotNull] MemberInfo memberInfo, ConfigurationSource? configurationSource)
        {
            var existingProperty = Metadata.FindServiceProperty(memberInfo);
            return existingProperty != null
                ? existingProperty.DeclaringEntityType == Metadata
                     || (configurationSource.HasValue
                        && configurationSource.Value.Overrides(existingProperty.GetConfigurationSource()))
                : CanAddServiceProperty(memberInfo, configurationSource);
        }

        private bool CanAddServiceProperty([NotNull] MemberInfo memberInfo, ConfigurationSource? configurationSource)
        {
            var propertyName = memberInfo.GetSimpleMemberName();
            if (!configurationSource.HasValue
                || IsIgnored(propertyName, configurationSource))
            {
                return false;
            }

            foreach (var conflictingProperty in Metadata.FindMembersInHierarchy(propertyName))
            {
                if (!configurationSource.Overrides(conflictingProperty.GetConfigurationSource())
                    && (!(conflictingProperty is ServiceProperty derivedServiceProperty)
                        || !memberInfo.IsOverridenBy(derivedServiceProperty.GetIdentifyingMemberInfo())))
                {
                    return false;
                }
            }

            return true;
        }

        private static InternalServicePropertyBuilder DetachServiceProperty(ServiceProperty serviceProperty)
        {
            var builder = serviceProperty?.Builder;
            if (builder == null)
            {
                return null;
            }
            serviceProperty.DeclaringEntityType.RemoveServiceProperty(serviceProperty);
            return builder;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool CanHaveNavigation([NotNull] string navigationName, ConfigurationSource? configurationSource)
            => !IsIgnored(navigationName, configurationSource)
                && Metadata.FindPropertiesInHierarchy(navigationName).Cast<IConventionPropertyBase>()
                    .Concat(Metadata.FindServicePropertiesInHierarchy(navigationName))
                    .Concat(Metadata.FindSkipNavigationsInHierarchy(navigationName))
                    .All(m => configurationSource.Overrides(m.GetConfigurationSource()));

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool IsIgnored([NotNull] string name, ConfigurationSource? configurationSource)
        {
            Check.NotEmpty(name, nameof(name));

            return configurationSource != ConfigurationSource.Explicit
                && !configurationSource.OverridesStrictly(Metadata.FindIgnoredConfigurationSource(name));
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalEntityTypeBuilder Ignore([NotNull] string name, ConfigurationSource configurationSource)
        {
            var ignoredConfigurationSource = Metadata.FindIgnoredConfigurationSource(name);
            if (ignoredConfigurationSource.HasValue)
            {
                if (ignoredConfigurationSource.Value.Overrides(configurationSource))
                {
                    return this;
                }
            }
            else if (!CanIgnore(name, configurationSource, shouldThrow: true))
            {
                return null;
            }

            using (Metadata.Model.ConventionDispatcher.DelayConventions())
            {
                Metadata.AddIgnored(name, configurationSource);

                var navigation = Metadata.FindNavigation(name);
                if (navigation != null)
                {
                    var foreignKey = navigation.ForeignKey;
                    Check.DebugAssert(navigation.DeclaringEntityType == Metadata, "navigation.DeclaringEntityType != Metadata");

                    var navigationConfigurationSource = navigation.GetConfigurationSource();
                    if (foreignKey.GetConfigurationSource() != navigationConfigurationSource)
                    {
                        var removedNavigation = foreignKey.Builder.HasNavigation(
                            (MemberInfo)null, navigation.IsOnDependent, configurationSource);
                        Check.DebugAssert(removedNavigation != null, "removedNavigation is null");
                    }
                    else
                    {
                        var removedForeignKey = foreignKey.DeclaringEntityType.Builder.HasNoRelationship(
                            foreignKey, configurationSource);
                        Check.DebugAssert(removedForeignKey != null, "removedForeignKey is null");
                    }
                }
                else
                {
                    var property = Metadata.FindProperty(name);
                    if (property != null)
                    {
                        Check.DebugAssert(property.DeclaringEntityType == Metadata, "property.DeclaringEntityType != Metadata");

                        var removedProperty = RemoveProperty(property, configurationSource);

                        Check.DebugAssert(removedProperty != null, "removedProperty is null");
                    }
                    else
                    {
                        var skipNavigation = Metadata.FindSkipNavigation(name);
                        if (skipNavigation != null)
                        {
                            var inverse = skipNavigation.Inverse;
                            if (inverse?.Builder != null
                                && inverse.DeclaringEntityType.Builder
                                    .CanRemoveSkipNavigation(inverse, configurationSource))
                            {
                                inverse.SetInverse(null, configurationSource);
                                inverse.DeclaringEntityType.RemoveSkipNavigation(inverse);
                            }

                            Check.DebugAssert(skipNavigation.DeclaringEntityType == Metadata, "skipNavigation.DeclaringEntityType != Metadata");

                            Metadata.RemoveSkipNavigation(skipNavigation);
                        }
                        else
                        {
                            var serviceProperty = Metadata.FindServiceProperty(name);
                            if (serviceProperty != null)
                            {
                                Check.DebugAssert(serviceProperty.DeclaringEntityType == Metadata, "serviceProperty.DeclaringEntityType != Metadata");

                                Metadata.RemoveServiceProperty(serviceProperty);
                            }
                        }
                    }
                }

                foreach (var derivedType in Metadata.GetDerivedTypes())
                {
                    var derivedIgnoredSource = derivedType.FindDeclaredIgnoredConfigurationSource(name);
                    if (derivedIgnoredSource.HasValue)
                    {
                        if (configurationSource.Overrides(derivedIgnoredSource))
                        {
                            derivedType.RemoveIgnored(name);
                        }

                        continue;
                    }

                    var derivedNavigation = derivedType.FindDeclaredNavigation(name);
                    if (derivedNavigation != null)
                    {
                        var foreignKey = derivedNavigation.ForeignKey;
                        if (foreignKey.GetConfigurationSource() != derivedNavigation.GetConfigurationSource())
                        {
                            if (derivedNavigation.GetConfigurationSource() != ConfigurationSource.Explicit)
                            {
                                foreignKey.Builder.HasNavigation(
                                    (MemberInfo)null, derivedNavigation.IsOnDependent, configurationSource);
                            }
                        }
                        else if (foreignKey.GetConfigurationSource() != ConfigurationSource.Explicit)
                        {
                            foreignKey.DeclaringEntityType.Builder.HasNoRelationship(
                                foreignKey, configurationSource);
                        }
                    }
                    else
                    {
                        var derivedProperty = derivedType.FindDeclaredProperty(name);
                        if (derivedProperty != null)
                        {
                            derivedType.Builder.RemoveProperty(
                                derivedProperty, configurationSource, canOverrideSameSource: configurationSource != ConfigurationSource.Explicit);
                        }
                        else
                        {
                            var skipNavigation = derivedType.FindDeclaredSkipNavigation(name);
                            if (skipNavigation != null)
                            {
                                var inverse = skipNavigation.Inverse;
                                if (inverse?.Builder != null
                                    && configurationSource != inverse.GetConfigurationSource()
                                    && inverse.DeclaringEntityType.Builder
                                        .CanRemoveSkipNavigation(inverse, configurationSource))
                                {
                                    inverse.SetInverse(null, configurationSource);
                                    inverse.DeclaringEntityType.RemoveSkipNavigation(inverse);
                                }

                                if (configurationSource.Overrides(skipNavigation.GetConfigurationSource())
                                    && skipNavigation.GetConfigurationSource() != ConfigurationSource.Explicit)
                                {
                                    derivedType.RemoveSkipNavigation(skipNavigation);
                                }
                            }
                            else
                            {
                                var derivedServiceProperty = derivedType.FindDeclaredServiceProperty(name);
                                if (derivedServiceProperty != null
                                    && configurationSource.Overrides(derivedServiceProperty.GetConfigurationSource())
                                    && derivedServiceProperty.GetConfigurationSource() != ConfigurationSource.Explicit)
                                {
                                    derivedType.RemoveServiceProperty(name);
                                }
                            }
                        }
                    }
                }
            }

            return this;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool CanIgnore([NotNull] string name, ConfigurationSource configurationSource)
            => CanIgnore(name, configurationSource, shouldThrow: false);

        private bool CanIgnore(string name, ConfigurationSource configurationSource, bool shouldThrow)
        {
            var ignoredConfigurationSource = Metadata.FindIgnoredConfigurationSource(name);
            if (ignoredConfigurationSource.HasValue)
            {
                return true;
            }

            var navigation = Metadata.FindNavigation(name);
            if (navigation != null)
            {
                if (navigation.DeclaringEntityType != Metadata)
                {
                    if (shouldThrow)
                    {
                        throw new InvalidOperationException(
                            CoreStrings.InheritedPropertyCannotBeIgnored(
                                name, Metadata.DisplayName(), navigation.DeclaringEntityType.DisplayName()));
                    }

                    return false;
                }

                if (!configurationSource.Overrides(navigation.GetConfigurationSource()))
                {
                    return false;
                }
            }
            else
            {
                var property = Metadata.FindProperty(name);
                if (property != null)
                {
                    if (property.DeclaringEntityType != Metadata)
                    {
                        if (shouldThrow)
                        {
                            throw new InvalidOperationException(
                                CoreStrings.InheritedPropertyCannotBeIgnored(
                                    name, Metadata.DisplayName(), property.DeclaringEntityType.DisplayName()));
                        }

                        return false;
                    }

                    if (!property.DeclaringEntityType.Builder.CanRemoveProperty(
                        property, configurationSource, canOverrideSameSource: true))
                    {
                        return false;
                    }
                }
                else
                {
                    var skipNavigation = Metadata.FindSkipNavigation(name);
                    if (skipNavigation != null)
                    {
                        if (skipNavigation.DeclaringEntityType != Metadata)
                        {
                            if (shouldThrow)
                            {
                                throw new InvalidOperationException(
                                    CoreStrings.InheritedPropertyCannotBeIgnored(
                                        name, Metadata.DisplayName(), skipNavigation.DeclaringEntityType.DisplayName()));
                            }

                            return false;
                        }

                        if (!configurationSource.Overrides(skipNavigation.GetConfigurationSource()))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        var serviceProperty = Metadata.FindServiceProperty(name);
                        if (serviceProperty != null)
                        {
                            if (serviceProperty.DeclaringEntityType != Metadata)
                            {
                                if (shouldThrow)
                                {
                                    throw new InvalidOperationException(
                                        CoreStrings.InheritedPropertyCannotBeIgnored(
                                            name, Metadata.DisplayName(), serviceProperty.DeclaringEntityType.DisplayName()));
                                }

                                return false;
                            }

                            if (!configurationSource.Overrides(serviceProperty.GetConfigurationSource()))
                            {
                                return false;
                            }
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalEntityTypeBuilder HasQueryFilter(
            [CanBeNull] LambdaExpression filter, ConfigurationSource configurationSource)
        {
            if (CanSetQueryFilter(filter, configurationSource))
            {
                Metadata.SetQueryFilter(filter, configurationSource);

                return this;
            }

            return null;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool CanSetQueryFilter([CanBeNull] LambdaExpression filter, ConfigurationSource configurationSource)
            => configurationSource.Overrides(Metadata.GetQueryFilterConfigurationSource())
                || Metadata.GetQueryFilter() == filter;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [Obsolete]
        public virtual InternalEntityTypeBuilder HasDefiningQuery(
            [CanBeNull] LambdaExpression query, ConfigurationSource configurationSource)
        {
            if (CanSetDefiningQuery(query, configurationSource))
            {
                Metadata.SetDefiningQuery(query, configurationSource);

                return this;
            }

            return null;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [Obsolete]
        public virtual bool CanSetDefiningQuery([CanBeNull] LambdaExpression query, ConfigurationSource configurationSource)
            => configurationSource.Overrides(Metadata.GetDefiningQueryConfigurationSource())
                || Metadata.GetDefiningQuery() == query;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalEntityTypeBuilder HasBaseType([CanBeNull] Type baseEntityType, ConfigurationSource configurationSource)
        {
            if (baseEntityType == null)
            {
                return HasBaseType((EntityType)null, configurationSource);
            }

            var baseType = ModelBuilder.Entity(baseEntityType, configurationSource);
            return baseType == null
                ? null
                : HasBaseType(baseType.Metadata, configurationSource);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalEntityTypeBuilder HasBaseType([CanBeNull] string baseEntityTypeName, ConfigurationSource configurationSource)
        {
            if (baseEntityTypeName == null)
            {
                return HasBaseType((EntityType)null, configurationSource);
            }

            var baseType = ModelBuilder.Entity(baseEntityTypeName, configurationSource);
            return baseType == null
                ? null
                : HasBaseType(baseType.Metadata, configurationSource);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalEntityTypeBuilder HasBaseType(
            [CanBeNull] EntityType baseEntityType, ConfigurationSource configurationSource)
        {
            if (Metadata.BaseType == baseEntityType)
            {
                Metadata.SetBaseType(baseEntityType, configurationSource);
                return this;
            }

            if (!CanSetBaseType(baseEntityType, configurationSource))
            {
                return null;
            }

            using (Metadata.Model.ConventionDispatcher.DelayConventions())
            {
                List<RelationshipSnapshot> detachedRelationships = null;
                List<InternalSkipNavigationBuilder> detachedSkipNavigations = null;
                PropertiesSnapshot detachedProperties = null;
                List<InternalServicePropertyBuilder> detachedServiceProperties = null;
                IReadOnlyList<(InternalKeyBuilder, ConfigurationSource?)> detachedKeys = null;
                // We use at least DataAnnotation as ConfigurationSource while removing to allow us
                // to remove metadata object which were defined in derived type
                // while corresponding annotations were present on properties in base type.
                var configurationSourceForRemoval = ConfigurationSource.DataAnnotation.Max(configurationSource);
                if (baseEntityType != null)
                {
                    var baseMemberNames = baseEntityType.GetMembers()
                        .ToDictionary(m => m.Name, m => (ConfigurationSource?)m.GetConfigurationSource());

                    var relationshipsToBeDetached =
                        FindConflictingMembers(Metadata.GetDerivedTypesInclusive().SelectMany(et => et.GetDeclaredNavigations()),
                            baseMemberNames,
                            n =>
                            {
                                var baseNavigation = baseEntityType.FindNavigation(n.Name);
                                return baseNavigation != null
                                    && n.TargetEntityType == baseNavigation.TargetEntityType;
                            },
                            n => n.ForeignKey.DeclaringEntityType.RemoveForeignKey(n.ForeignKey))
                            ?.Select(n => n.ForeignKey).ToHashSet();

                    foreach (var key in Metadata.GetDeclaredKeys().ToList())
                    {
                        foreach (var referencingForeignKey in key.GetReferencingForeignKeys().ToList())
                        {
                            var navigationToDependent = referencingForeignKey.PrincipalToDependent;
                            if (navigationToDependent != null
                                && baseMemberNames.TryGetValue(navigationToDependent.Name, out var baseConfigurationSource)
                                && baseConfigurationSource == ConfigurationSource.Explicit
                                && configurationSource == ConfigurationSource.Explicit
                                && navigationToDependent.GetConfigurationSource() == ConfigurationSource.Explicit)
                            {
                                var baseProperty = baseEntityType.FindMembersInHierarchy(navigationToDependent.Name).Single();
                                if (!(baseProperty is INavigation))
                                {
                                    throw new InvalidOperationException(
                                        CoreStrings.DuplicatePropertiesOnBase(
                                            Metadata.DisplayName(),
                                            baseEntityType.DisplayName(),
                                            navigationToDependent.DeclaringType.DisplayName(),
                                            navigationToDependent.Name,
                                            baseProperty.DeclaringType.DisplayName(),
                                            baseProperty.Name));
                                }
                            }

                            if (relationshipsToBeDetached == null)
                            {
                                relationshipsToBeDetached = new HashSet<ForeignKey>();
                            }

                            relationshipsToBeDetached.Add(referencingForeignKey);
                        }
                    }

                    if (relationshipsToBeDetached != null)
                    {
                        detachedRelationships = new List<RelationshipSnapshot>();
                        foreach (var relationshipToBeDetached in relationshipsToBeDetached)
                        {
                            detachedRelationships.Add(DetachRelationship(relationshipToBeDetached));
                        }
                    }

                    var foreignKeysUsingKeyProperties = Metadata.GetDerivedTypesInclusive()
                        .SelectMany(t => t.GetDeclaredForeignKeys())
                        .Where(fk => fk.Properties.Any(p => baseEntityType.FindProperty(p.Name)?.IsKey() == true));

                    foreach (var foreignKeyUsingKeyProperties in foreignKeysUsingKeyProperties.ToList())
                    {
                        foreignKeyUsingKeyProperties.Builder.HasForeignKey((IReadOnlyList<Property>)null, configurationSourceForRemoval);
                    }

                    var skipNavigationsToDetach =
                        FindConflictingMembers(Metadata.GetDerivedTypesInclusive().SelectMany(et => et.GetDeclaredSkipNavigations()),
                        baseMemberNames,
                        n =>
                        {
                            var baseNavigation = baseEntityType.FindSkipNavigation(n.Name);
                            return baseNavigation != null
                                && n.TargetEntityType == baseNavigation.TargetEntityType;
                        },
                        n => n.DeclaringEntityType.RemoveSkipNavigation(n));

                    if (skipNavigationsToDetach != null)
                    {
                        detachedSkipNavigations = new List<InternalSkipNavigationBuilder>();
                        foreach (var skipNavigation in skipNavigationsToDetach)
                        {
                            detachedSkipNavigations.Add(DetachSkipNavigation(skipNavigation));
                        }
                    }

                    detachedKeys = DetachKeys(Metadata.GetDeclaredKeys());

                    Metadata.SetIsKeyless(false, configurationSource);

                    var propertiesToDetach =
                        FindConflictingMembers(Metadata.GetDerivedTypesInclusive().SelectMany(et => et.GetDeclaredProperties()),
                        baseMemberNames,
                        n => baseEntityType.FindProperty(n.Name) != null,
                        p => p.DeclaringEntityType.Builder.RemoveProperty(p, ConfigurationSource.Explicit));

                    if (propertiesToDetach != null)
                    {
                        detachedProperties = DetachProperties(propertiesToDetach);
                    }

                    var servicePropertiesToDetach =
                        FindConflictingMembers(Metadata.GetDerivedTypesInclusive().SelectMany(et => et.GetDeclaredServiceProperties()),
                        baseMemberNames,
                        n => baseEntityType.FindServiceProperty(n.Name) != null,
                        p => p.DeclaringEntityType.RemoveServiceProperty(p));

                    if (servicePropertiesToDetach != null)
                    {
                        detachedServiceProperties = new List<InternalServicePropertyBuilder>();
                        foreach (var serviceProperty in servicePropertiesToDetach)
                        {
                            detachedServiceProperties.Add(DetachServiceProperty(serviceProperty));
                        }
                    }

                    foreach (var ignoredMember in Metadata.GetIgnoredMembers().ToList())
                    {
                        if (baseEntityType.FindIgnoredConfigurationSource(ignoredMember)
                            .Overrides(Metadata.FindDeclaredIgnoredConfigurationSource(ignoredMember)))
                        {
                            Metadata.RemoveIgnored(ignoredMember);
                        }
                    }

                    baseEntityType.UpdateConfigurationSource(configurationSource);
                }

                List<InternalIndexBuilder> detachedIndexes = null;
                HashSet<Property> removedInheritedPropertiesToDuplicate = null;
                if (Metadata.BaseType != null)
                {
                    var removedInheritedProperties = new HashSet<Property>(
                        Metadata.BaseType.GetProperties()
                            .Where(p => baseEntityType == null || baseEntityType.FindProperty(p.Name) != p));
                    if (removedInheritedProperties.Count != 0)
                    {
                        removedInheritedPropertiesToDuplicate = new HashSet<Property>();
                        List<ForeignKey> relationshipsToBeDetached = null;
                        foreach (var foreignKey in Metadata.GetDerivedTypesInclusive()
                            .SelectMany(t => t.GetDeclaredForeignKeys()))
                        {
                            var shouldBeDetached = false;
                            foreach (var property in foreignKey.Properties)
                            {
                                if (removedInheritedProperties.Contains(property))
                                {
                                    removedInheritedPropertiesToDuplicate.Add(property);
                                    shouldBeDetached = true;
                                }
                            }

                            if (!shouldBeDetached)
                            {
                                continue;
                            }

                            if (relationshipsToBeDetached == null)
                            {
                                relationshipsToBeDetached = new List<ForeignKey>();
                            }

                            relationshipsToBeDetached.Add(foreignKey);
                        }

                        foreach (var key in Metadata.GetKeys())
                        {
                            if (key.ReferencingForeignKeys == null
                                || !key.Properties.Any(p => removedInheritedProperties.Contains(p)))
                            {
                                continue;
                            }

                            foreach (var referencingForeignKey in key.ReferencingForeignKeys.ToList())
                            {
                                if (Metadata.IsAssignableFrom(referencingForeignKey.PrincipalEntityType))
                                {
                                    if (relationshipsToBeDetached == null)
                                    {
                                        relationshipsToBeDetached = new List<ForeignKey>();
                                    }

                                    relationshipsToBeDetached.Add(referencingForeignKey);
                                }
                            }
                        }

                        if (relationshipsToBeDetached != null)
                        {
                            detachedRelationships = new List<RelationshipSnapshot>();
                            foreach (var relationshipToBeDetached in relationshipsToBeDetached)
                            {
                                detachedRelationships.Add(DetachRelationship(relationshipToBeDetached));
                            }
                        }

                        List<Index> indexesToBeDetached = null;
                        foreach (var index in Metadata.GetDerivedTypesInclusive().SelectMany(e => e.GetDeclaredIndexes()))
                        {
                            var shouldBeDetached = false;
                            foreach (var property in index.Properties)
                            {
                                if (removedInheritedProperties.Contains(property))
                                {
                                    removedInheritedPropertiesToDuplicate.Add(property);
                                    shouldBeDetached = true;
                                }
                            }

                            if (!shouldBeDetached)
                            {
                                continue;
                            }

                            if (indexesToBeDetached == null)
                            {
                                indexesToBeDetached = new List<Index>();
                            }

                            indexesToBeDetached.Add(index);
                        }

                        if (indexesToBeDetached != null)
                        {
                            detachedIndexes = new List<InternalIndexBuilder>();
                            foreach (var indexToBeDetached in indexesToBeDetached)
                            {
                                detachedIndexes.Add(DetachIndex(indexToBeDetached));
                            }
                        }
                    }
                }

                Metadata.SetBaseType(baseEntityType, configurationSource);

                if (removedInheritedPropertiesToDuplicate != null)
                {
                    foreach (var property in removedInheritedPropertiesToDuplicate)
                    {
                        property.Builder?.Attach(this);
                    }
                }

                if (detachedServiceProperties != null)
                {
                    foreach (var detachedServiceProperty in detachedServiceProperties)
                    {
                        detachedServiceProperty.Attach(detachedServiceProperty.Metadata.DeclaringEntityType.Builder);
                    }
                }

                detachedProperties?.Attach(this);

                if (detachedKeys != null)
                {
                    foreach (var detachedKeyTuple in detachedKeys)
                    {
                        detachedKeyTuple.Item1.Attach(Metadata.RootType().Builder, detachedKeyTuple.Item2);
                    }
                }

                if (detachedIndexes != null)
                {
                    foreach (var detachedIndex in detachedIndexes)
                    {
                        detachedIndex.Attach(detachedIndex.Metadata.DeclaringEntityType.Builder);
                    }
                }

                if (detachedSkipNavigations != null)
                {
                    foreach (var detachedSkipNavigation in detachedSkipNavigations)
                    {
                        detachedSkipNavigation.Attach();
                    }
                }

                if (detachedRelationships != null)
                {
                    foreach (var detachedRelationship in detachedRelationships)
                    {
                        detachedRelationship.Attach();
                    }
                }
            }

            return this;

            List<T> FindConflictingMembers<T>(
                IEnumerable<T> derivedMembers,
                Dictionary<string, ConfigurationSource?> baseMemberNames,
                Func<T, bool> compatibleWithBaseMember,
                Action<T> removeMember)
                where T : PropertyBase
            {
                List<T> membersToBeDetached = null;
                List<T> membersToBeRemoved = null;
                foreach (var member in derivedMembers)
                {
                    ConfigurationSource? baseConfigurationSource = null;
                    if ((!member.GetConfigurationSource().OverridesStrictly(
                            baseEntityType.FindIgnoredConfigurationSource(member.Name))
                            && member.GetConfigurationSource() != ConfigurationSource.Explicit)
                        || (baseMemberNames.TryGetValue(member.Name, out baseConfigurationSource)
                            && baseConfigurationSource.Overrides(member.GetConfigurationSource())
                            && !compatibleWithBaseMember(member)))
                    {
                        if (baseConfigurationSource == ConfigurationSource.Explicit
                            && configurationSource == ConfigurationSource.Explicit
                            && member.GetConfigurationSource() == ConfigurationSource.Explicit)
                        {
                            continue;
                        }

                        if (membersToBeRemoved == null)
                        {
                            membersToBeRemoved = new List<T>();
                        }

                        membersToBeRemoved.Add(member);
                        continue;
                    }

                    if (baseConfigurationSource != null)
                    {
                        if (membersToBeDetached == null)
                        {
                            membersToBeDetached = new List<T>();
                        }

                        membersToBeDetached.Add(member);
                    }
                }

                if (membersToBeRemoved != null)
                {
                    foreach (var memberToBeRemoved in membersToBeRemoved)
                    {
                        removeMember(memberToBeRemoved);
                    }
                }

                return membersToBeDetached;
            }
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool CanSetBaseType([NotNull] EntityType baseEntityType, ConfigurationSource configurationSource)
        {
            if (Metadata.BaseType == baseEntityType
                || configurationSource == ConfigurationSource.Explicit)
            {
                return true;
            }

            if (!configurationSource.Overrides(Metadata.GetBaseTypeConfigurationSource()))
            {
                return false;
            }

            if (baseEntityType == null)
            {
                return true;
            }

            var configurationSourceForRemoval = ConfigurationSource.DataAnnotation.Max(configurationSource);
            if (Metadata.GetDeclaredKeys().Any(k => !configurationSourceForRemoval.Overrides(k.GetConfigurationSource()))
                || (Metadata.IsKeyless && !configurationSource.Overrides(Metadata.GetIsKeylessConfigurationSource())))
            {
                return false;
            }

            if (Metadata.GetDerivedTypesInclusive()
                .SelectMany(t => t.GetDeclaredForeignKeys())
                .Where(fk => fk.Properties.Any(p => baseEntityType.FindProperty(p.Name)?.IsKey() == true))
                .Any(fk => !configurationSourceForRemoval.Overrides(fk.GetPropertiesConfigurationSource())))
            {
                return false;
            }

            var baseMembers = baseEntityType.GetMembers()
                .Where(m => m.GetConfigurationSource() == ConfigurationSource.Explicit)
                .ToDictionary(m => m.Name);

            foreach (var derivedMember in Metadata.GetDerivedTypesInclusive().SelectMany(et => et.GetDeclaredMembers()))
            {
                if (derivedMember.GetConfigurationSource() == ConfigurationSource.Explicit
                    && baseMembers.TryGetValue(derivedMember.Name, out var baseMember))
                {
                    if (derivedMember is IProperty)
                    {
                        return baseMember is IProperty;
                    }

                    if (derivedMember is INavigation derivedNavigation)
                    {
                        return baseMember is INavigation baseNavigation
                            && derivedNavigation.TargetEntityType == baseNavigation.TargetEntityType;
                    }

                    if (derivedMember is IServiceProperty)
                    {
                        return baseMember is IServiceProperty;
                    }

                    if (derivedMember is ISkipNavigation derivedSkipNavigation)
                    {
                        return baseMember is ISkipNavigation baseSkipNavigation
                            && derivedSkipNavigation.TargetEntityType == baseSkipNavigation.TargetEntityType;
                    }
                }
            }

            return true;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public static PropertiesSnapshot DetachProperties([NotNull] IReadOnlyList<Property> propertiesToDetach)
        {
            if (propertiesToDetach.Count == 0)
            {
                return null;
            }

            List<RelationshipSnapshot> detachedRelationships = null;
            foreach (var propertyToDetach in propertiesToDetach)
            {
                foreach (var relationship in propertyToDetach.GetContainingForeignKeys().ToList())
                {
                    if (detachedRelationships == null)
                    {
                        detachedRelationships = new List<RelationshipSnapshot>();
                    }

                    detachedRelationships.Add(DetachRelationship(relationship));
                }
            }

            var detachedIndexes = DetachIndexes(propertiesToDetach.SelectMany(p => p.GetContainingIndexes()).Distinct());

            var keysToDetach = propertiesToDetach.SelectMany(p => p.GetContainingKeys()).Distinct().ToList();
            foreach (var key in keysToDetach)
            {
                foreach (var referencingForeignKey in key.GetReferencingForeignKeys().ToList())
                {
                    if (detachedRelationships == null)
                    {
                        detachedRelationships = new List<RelationshipSnapshot>();
                    }

                    detachedRelationships.Add(DetachRelationship(referencingForeignKey));
                }
            }

            var detachedKeys = DetachKeys(keysToDetach);

            var detachedProperties = new List<InternalPropertyBuilder>();
            foreach (var propertyToDetach in propertiesToDetach)
            {
                var property = propertyToDetach.DeclaringEntityType.FindDeclaredProperty(propertyToDetach.Name);
                if (property != null)
                {
                    var entityTypeBuilder = property.DeclaringEntityType.Builder;
                    var propertyBuilder = property.Builder;
                    // Reset convention configuration
                    propertyBuilder.ValueGenerated(null, ConfigurationSource.Convention);
                    propertyBuilder.AfterSave(null, ConfigurationSource.Convention);
                    propertyBuilder.BeforeSave(null, ConfigurationSource.Convention);
                    ConfigurationSource? removedConfigurationSource;
                    if (entityTypeBuilder != null)
                    {
                        removedConfigurationSource = entityTypeBuilder
                            .RemoveProperty(property, property.GetConfigurationSource());
                    }
                    else
                    {
                        removedConfigurationSource = property.GetConfigurationSource();
                        property.DeclaringEntityType.RemoveProperty(property.Name);
                    }

                    Check.DebugAssert(removedConfigurationSource.HasValue, "removedConfigurationSource.HasValue is false");
                    detachedProperties.Add(propertyBuilder);
                }
            }

            return new PropertiesSnapshot(detachedProperties, detachedIndexes, detachedKeys, detachedRelationships);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool CanRemoveForeignKey([NotNull] ForeignKey foreignKey, ConfigurationSource configurationSource)
        {
            Check.DebugAssert(foreignKey.DeclaringEntityType == Metadata, "foreignKey.DeclaringEntityType != Metadata");

            return configurationSource.Overrides(foreignKey.GetConfigurationSource());
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool CanRemoveSkipNavigation([NotNull] SkipNavigation skipNavigation, ConfigurationSource? configurationSource)
        {
            Check.DebugAssert(skipNavigation.DeclaringEntityType == Metadata, "skipNavigation.DeclaringEntityType != Metadata");

            return configurationSource.Overrides(skipNavigation.GetConfigurationSource());
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public static RelationshipSnapshot DetachRelationship([NotNull] ForeignKey foreignKey)
            => DetachRelationship(foreignKey, false);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public static RelationshipSnapshot DetachRelationship([NotNull] ForeignKey foreignKey, bool includeDefinedType)
        {
            var detachedBuilder = foreignKey.Builder;
            var referencingSkipNavigations = foreignKey.ReferencingSkipNavigations?
                .Select(s => (s, s.GetForeignKeyConfigurationSource().Value)).ToList();
            var relationshipConfigurationSource = foreignKey.DeclaringEntityType.Builder
                .HasNoRelationship(foreignKey, foreignKey.GetConfigurationSource());
            Check.DebugAssert(relationshipConfigurationSource != null, "relationshipConfigurationSource is null");

            EntityType.Snapshot definedSnapshot = null;
            if (includeDefinedType)
            {
                var dependentEntityType = foreignKey.DeclaringEntityType;
                if (dependentEntityType.DefiningEntityType == foreignKey.PrincipalEntityType
                    && dependentEntityType.DefiningNavigationName == foreignKey.PrincipalToDependent?.Name)
                {
                    definedSnapshot = DetachAllMembers(dependentEntityType);
                    dependentEntityType.Model.Builder.HasNoEntityType(dependentEntityType, ConfigurationSource.Explicit);
                }
            }

            return new RelationshipSnapshot(detachedBuilder, definedSnapshot, referencingSkipNavigations);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalEntityTypeBuilder HasNoRelationship(
            [NotNull] ForeignKey foreignKey,
            ConfigurationSource configurationSource)
        {
            var currentConfigurationSource = foreignKey.GetConfigurationSource();
            if (!configurationSource.Overrides(currentConfigurationSource))
            {
                return null;
            }

            if (foreignKey.ReferencingSkipNavigations != null)
            {
                foreach (var referencingSkipNavigation in foreignKey.ReferencingSkipNavigations.ToList())
                {
                    Check.DebugAssert(currentConfigurationSource.Overrides(referencingSkipNavigation.GetForeignKeyConfigurationSource()),
                        "Setting the FK on the skip navigation should upgrade the configuration source");

                    referencingSkipNavigation.Builder.HasForeignKey(null, configurationSource);
                }
            }

            Metadata.RemoveForeignKey(foreignKey);

            RemoveUnusedShadowProperties(foreignKey.Properties);
            foreignKey.PrincipalKey.DeclaringEntityType.Builder?.RemoveKeyIfUnused(foreignKey.PrincipalKey);

            return this;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public static EntityType.Snapshot DetachAllMembers([NotNull] EntityType entityType)
        {
            if (entityType.Builder == null)
            {
                return null;
            }

            if (entityType.HasDefiningNavigation())
            {
                entityType.Model.AddDetachedEntityType(
                    entityType.Name, entityType.DefiningNavigationName, entityType.DefiningEntityType.Name);
            }

            List<RelationshipSnapshot> detachedRelationships = null;
            foreach (var relationshipToBeDetached in entityType.GetDeclaredForeignKeys().ToList())
            {
                if (detachedRelationships == null)
                {
                    detachedRelationships = new List<RelationshipSnapshot>();
                }

                var detachedRelationship = DetachRelationship(relationshipToBeDetached);
                if (detachedRelationship.Relationship.Metadata.GetConfigurationSource().Overrides(ConfigurationSource.DataAnnotation)
                    || relationshipToBeDetached.IsOwnership)
                {
                    detachedRelationships.Add(detachedRelationship);
                }
            }

            List<InternalSkipNavigationBuilder> detachedSkipNavigations = null;
            foreach (var skipNavigationsToBeDetached in entityType.GetDeclaredSkipNavigations().ToList())
            {
                if (detachedSkipNavigations == null)
                {
                    detachedSkipNavigations = new List<InternalSkipNavigationBuilder>();
                }
                detachedSkipNavigations.Add(DetachSkipNavigation(skipNavigationsToBeDetached));
            }

            List<(InternalKeyBuilder, ConfigurationSource?)> detachedKeys = null;
            foreach (var keyToDetach in entityType.GetDeclaredKeys().ToList())
            {
                foreach (var relationshipToBeDetached in keyToDetach.GetReferencingForeignKeys().ToList())
                {
                    if (detachedRelationships == null)
                    {
                        detachedRelationships = new List<RelationshipSnapshot>();
                    }

                    var detachedRelationship = DetachRelationship(relationshipToBeDetached, true);
                    if (detachedRelationship.Relationship.Metadata.GetConfigurationSource().Overrides(ConfigurationSource.DataAnnotation)
                        || relationshipToBeDetached.IsOwnership)
                    {
                        detachedRelationships.Add(detachedRelationship);
                    }
                }

                if (keyToDetach.Builder == null)
                {
                    continue;
                }

                if (detachedKeys == null)
                {
                    detachedKeys = new List<(InternalKeyBuilder, ConfigurationSource?)>();
                }

                var detachedKey = DetachKey(keyToDetach);
                if (detachedKey.Item1.Metadata.GetConfigurationSource().Overrides(ConfigurationSource.Explicit))
                {
                    detachedKeys.Add(detachedKey);
                }
            }

            List<InternalIndexBuilder> detachedIndexes = null;
            foreach (var index in entityType.GetDeclaredIndexes().ToList())
            {
                if (detachedIndexes == null)
                {
                    detachedIndexes = new List<InternalIndexBuilder>();
                }

                var detachedIndex = DetachIndex(index);
                if (detachedIndex.Metadata.GetConfigurationSource().Overrides(ConfigurationSource.Explicit))
                {
                    detachedIndexes.Add(detachedIndex);
                }
            }

            var detachedProperties = DetachProperties(entityType.GetDeclaredProperties().ToList());

            List<InternalServicePropertyBuilder> detachedServiceProperties = null;
            foreach (var servicePropertiesToBeDetached in entityType.GetDeclaredServiceProperties().ToList())
            {
                if (detachedServiceProperties == null)
                {
                    detachedServiceProperties = new List<InternalServicePropertyBuilder>();
                }
                detachedServiceProperties.Add(DetachServiceProperty(servicePropertiesToBeDetached));
            }

            return new EntityType.Snapshot(
                entityType,
                detachedProperties,
                detachedIndexes,
                detachedKeys,
                detachedRelationships,
                detachedSkipNavigations,
                detachedServiceProperties);
        }

        private void RemoveKeyIfUnused(Key key, ConfigurationSource configurationSource = ConfigurationSource.Convention)
        {
            if (Metadata.FindPrimaryKey() == key)
            {
                return;
            }

            if (key.GetReferencingForeignKeys().Any())
            {
                return;
            }

            HasNoKey(key, configurationSource);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalEntityTypeBuilder RemoveUnusedShadowProperties<T>(
            [NotNull] IReadOnlyList<T> properties, ConfigurationSource configurationSource = ConfigurationSource.Convention)
            where T : class, IProperty
        {
            foreach (var property in properties)
            {
                if (property?.IsShadowProperty() == true)
                {
                    RemovePropertyIfUnused((Property)(object)property, configurationSource);
                }
            }

            return this;
        }

        private static void RemovePropertyIfUnused(Property property, ConfigurationSource configurationSource)
        {
            if (property.Builder == null
                || !property.DeclaringEntityType.Builder.CanRemoveProperty(property, configurationSource)
                || property.GetContainingIndexes().Any()
                || property.GetContainingForeignKeys().Any()
                || property.GetContainingKeys().Any())
            {
                return;
            }

            var removedProperty = property.DeclaringEntityType.RemoveProperty(property.Name);
            Check.DebugAssert(removedProperty == property, "removedProperty != property");
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalIndexBuilder HasIndex([NotNull] IReadOnlyList<string> propertyNames, ConfigurationSource configurationSource)
            => HasIndex(GetOrCreateProperties(propertyNames, configurationSource), configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalIndexBuilder HasIndex(
            [NotNull] IReadOnlyList<string> propertyNames,
            [NotNull] string name,
            ConfigurationSource configurationSource)
            => HasIndex(GetOrCreateProperties(propertyNames, configurationSource), name, configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalIndexBuilder HasIndex(
            [NotNull] IReadOnlyList<MemberInfo> clrMembers, ConfigurationSource configurationSource)
            => HasIndex(GetOrCreateProperties(clrMembers, configurationSource), configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalIndexBuilder HasIndex(
            [NotNull] IReadOnlyList<MemberInfo> clrMembers,
            [NotNull] string name,
            ConfigurationSource configurationSource)
            => HasIndex(GetOrCreateProperties(clrMembers, configurationSource), name, configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalIndexBuilder HasIndex(
            [CanBeNull] IReadOnlyList<Property> properties, ConfigurationSource configurationSource)
        {
            if (properties == null)
            {
                return null;
            }

            List<InternalIndexBuilder> detachedIndexes = null;
            var existingIndex = Metadata.FindIndex(properties);
            if (existingIndex == null)
            {
                detachedIndexes = Metadata.FindDerivedIndexes(properties).ToList().Select(DetachIndex).ToList();
            }
            else if (existingIndex.DeclaringEntityType != Metadata)
            {
                return existingIndex.DeclaringEntityType.Builder.HasIndex(existingIndex, properties, null, configurationSource);
            }

            var indexBuilder = HasIndex(existingIndex, properties, null, configurationSource);

            if (detachedIndexes != null)
            {
                foreach (var detachedIndex in detachedIndexes)
                {
                    detachedIndex.Attach(detachedIndex.Metadata.DeclaringEntityType.Builder);
                }
            }

            return indexBuilder;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalIndexBuilder HasIndex(
            [CanBeNull] IReadOnlyList<Property> properties,
            [NotNull] string name,
            ConfigurationSource configurationSource)
        {
            Check.NotEmpty(name, nameof(name));

            if (properties == null)
            {
                return null;
            }

            List<InternalIndexBuilder> detachedIndexes = null;

            var existingIndex = Metadata.FindIndex(name);
            if (existingIndex != null
                && !existingIndex.Properties.SequenceEqual(properties))
            {
                // use existing index only if properties match
                existingIndex = null;
            }

            if (existingIndex == null)
            {
                detachedIndexes = Metadata.FindDerivedIndexes(name)
                    .Where(i => i.Properties.SequenceEqual(properties))
                    .ToList().Select(DetachIndex).ToList();
            }
            else if (existingIndex.DeclaringEntityType != Metadata)
            {
                return existingIndex.DeclaringEntityType.Builder.HasIndex(existingIndex, properties, name, configurationSource);
            }

            var indexBuilder = HasIndex(existingIndex, properties, name, configurationSource);

            if (detachedIndexes != null)
            {
                foreach (var detachedIndex in detachedIndexes)
                {
                    detachedIndex.Attach(detachedIndex.Metadata.DeclaringEntityType.Builder);
                }
            }

            return indexBuilder;
        }

        private InternalIndexBuilder HasIndex(
            Index index,
            IReadOnlyList<Property> properties,
            string name,
            ConfigurationSource configurationSource)
        {
            if (index == null)
            {
                if (name == null)
                {
                    index = Metadata.AddIndex(properties, configurationSource);
                }
                else
                {
                    index = Metadata.AddIndex(properties, name, configurationSource);
                }
            }
            else
            {
                index.UpdateConfigurationSource(configurationSource);
            }

            return index?.Builder;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalEntityTypeBuilder HasNoIndex([NotNull] Index index, ConfigurationSource configurationSource)
        {
            var currentConfigurationSource = index.GetConfigurationSource();
            if (!configurationSource.Overrides(currentConfigurationSource))
            {
                return null;
            }

            var removedIndex = index.Name == null
                ? Metadata.RemoveIndex(index.Properties)
                : Metadata.RemoveIndex(index.Name);
            Check.DebugAssert(removedIndex == index, "removedIndex != index");

            RemoveUnusedShadowProperties(index.Properties);

            return this;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool CanRemoveIndex([NotNull] Index index, ConfigurationSource configurationSource)
            => configurationSource.Overrides(index.GetConfigurationSource());

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public static List<InternalIndexBuilder> DetachIndexes([NotNull] IEnumerable<Index> indexesToDetach)
        {
            var indexesToDetachList = (indexesToDetach as List<Index>) ?? indexesToDetach.ToList();
            if (indexesToDetachList.Count == 0)
            {
                return null;
            }

            var detachedIndexes = new List<InternalIndexBuilder>();
            foreach (var indexToDetach in indexesToDetachList)
            {
                var detachedIndex = DetachIndex(indexToDetach);
                detachedIndexes.Add(detachedIndex);
            }

            return detachedIndexes;
        }

        private static InternalIndexBuilder DetachIndex(Index indexToDetach)
        {
            var entityTypeBuilder = indexToDetach.DeclaringEntityType.Builder;
            var indexBuilder = indexToDetach.Builder;
            var removedConfigurationSource = entityTypeBuilder.HasNoIndex(indexToDetach, indexToDetach.GetConfigurationSource());
            Check.DebugAssert(removedConfigurationSource != null, "removedConfigurationSource is null");
            return indexBuilder;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalForeignKeyBuilder HasRelationship(
            [NotNull] string principalEntityTypeName,
            [NotNull] IReadOnlyList<string> propertyNames,
            ConfigurationSource configurationSource)
        {
            Check.NotEmpty(principalEntityTypeName, nameof(principalEntityTypeName));
            Check.NotEmpty(propertyNames, nameof(propertyNames));

            var principalTypeBuilder = ModelBuilder.Entity(principalEntityTypeName, configurationSource);
            var principalKey = principalTypeBuilder?.Metadata.FindPrimaryKey();
            return principalTypeBuilder == null
                ? null
                : HasForeignKey(
                    principalTypeBuilder.Metadata,
                    GetOrCreateProperties(
                        propertyNames, configurationSource, principalKey?.Properties, useDefaultType: principalKey == null),
                    null,
                    configurationSource);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalForeignKeyBuilder HasRelationship(
            [NotNull] string principalEntityTypeName,
            [NotNull] IReadOnlyList<string> propertyNames,
            [NotNull] Key principalKey,
            ConfigurationSource configurationSource)
        {
            Check.NotEmpty(principalEntityTypeName, nameof(principalEntityTypeName));
            Check.NotEmpty(propertyNames, nameof(propertyNames));

            var principalTypeBuilder = ModelBuilder.Entity(principalEntityTypeName, configurationSource);
            return principalTypeBuilder == null
                ? null
                : HasForeignKey(
                    principalTypeBuilder.Metadata,
                    GetOrCreateProperties(propertyNames, configurationSource, principalKey.Properties),
                    principalKey,
                    configurationSource);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalForeignKeyBuilder HasRelationship(
            [NotNull] Type principalClrType,
            [NotNull] IReadOnlyList<MemberInfo> clrMembers,
            ConfigurationSource configurationSource)
        {
            Check.NotNull(principalClrType, nameof(principalClrType));
            Check.NotEmpty(clrMembers, nameof(clrMembers));

            var principalTypeBuilder = ModelBuilder.Entity(principalClrType, configurationSource);
            return principalTypeBuilder == null
                ? null
                : HasForeignKey(
                    principalTypeBuilder.Metadata,
                    GetOrCreateProperties(clrMembers, configurationSource),
                    null,
                    configurationSource);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalForeignKeyBuilder HasRelationship(
            [NotNull] Type principalClrType,
            [NotNull] IReadOnlyList<MemberInfo> clrMembers,
            [NotNull] Key principalKey,
            ConfigurationSource configurationSource)
        {
            Check.NotNull(principalClrType, nameof(principalClrType));
            Check.NotEmpty(clrMembers, nameof(clrMembers));

            var principalTypeBuilder = ModelBuilder.Entity(principalClrType, configurationSource);
            return principalTypeBuilder == null
                ? null
                : HasForeignKey(
                    principalTypeBuilder.Metadata,
                    GetOrCreateProperties(clrMembers, configurationSource),
                    principalKey,
                    configurationSource);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalForeignKeyBuilder HasRelationship(
            [NotNull] EntityType principalEntityType,
            [NotNull] IReadOnlyList<Property> dependentProperties,
            ConfigurationSource configurationSource)
            => HasForeignKey(
                principalEntityType,
                GetActualProperties(dependentProperties, configurationSource),
                null,
                configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalForeignKeyBuilder HasRelationship(
            [NotNull] EntityType principalEntityType,
            [NotNull] IReadOnlyList<Property> dependentProperties,
            [CanBeNull] Key principalKey,
            ConfigurationSource configurationSource)
            => HasForeignKey(
                principalEntityType,
                GetActualProperties(dependentProperties, configurationSource),
                principalKey,
                configurationSource);

        private InternalForeignKeyBuilder HasForeignKey(
            EntityType principalEntityType,
            IReadOnlyList<Property> dependentProperties,
            Key principalKey,
            ConfigurationSource configurationSource)
        {
            if (dependentProperties == null)
            {
                return null;
            }

            var newRelationship = HasRelationshipInternal(principalEntityType, principalKey, configurationSource);

            var relationship = newRelationship.HasForeignKey(dependentProperties, configurationSource);
            if (relationship == null
                && newRelationship.Metadata.Builder != null)
            {
                HasNoRelationship(newRelationship.Metadata, configurationSource);
            }

            newRelationship = relationship;

            return newRelationship;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalForeignKeyBuilder HasRelationship(
            [NotNull] EntityType targetEntityType,
            [CanBeNull] string navigationName,
            ConfigurationSource configurationSource,
            bool? targetIsPrincipal = null)
            => HasRelationship(
                Check.NotNull(targetEntityType, nameof(targetEntityType)),
                MemberIdentity.Create(navigationName),
                null,
                targetIsPrincipal,
                configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalForeignKeyBuilder HasRelationship(
            [NotNull] EntityType targetEntityType,
            [CanBeNull] MemberInfo navigationMember,
            ConfigurationSource configurationSource,
            bool? targetIsPrincipal = null)
            => HasRelationship(
                Check.NotNull(targetEntityType, nameof(targetEntityType)),
                MemberIdentity.Create(navigationMember),
                null,
                targetIsPrincipal,
                configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalForeignKeyBuilder HasRelationship(
            [NotNull] EntityType targetEntityType,
            [CanBeNull] string navigationToTargetName,
            [CanBeNull] string inverseNavigationName,
            ConfigurationSource configurationSource,
            bool setTargetAsPrincipal = false)
            => HasRelationship(
                Check.NotNull(targetEntityType, nameof(targetEntityType)),
                MemberIdentity.Create(navigationToTargetName),
                MemberIdentity.Create(inverseNavigationName),
                setTargetAsPrincipal ? true : (bool?)null,
                configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalForeignKeyBuilder HasRelationship(
            [NotNull] EntityType targetEntityType,
            [CanBeNull] MemberInfo navigationToTarget,
            [CanBeNull] MemberInfo inverseNavigation,
            ConfigurationSource configurationSource,
            bool setTargetAsPrincipal = false)
            => HasRelationship(
                Check.NotNull(targetEntityType, nameof(targetEntityType)),
                MemberIdentity.Create(navigationToTarget),
                MemberIdentity.Create(inverseNavigation),
                setTargetAsPrincipal ? true : (bool?)null,
                configurationSource);

        private InternalForeignKeyBuilder HasRelationship(
            EntityType targetEntityType,
            MemberIdentity? navigationToTarget,
            MemberIdentity? inverseNavigation,
            bool? targetIsPrincipal,
            ConfigurationSource configurationSource,
            bool? required = null)
        {
            Check.DebugAssert(
                navigationToTarget != null || inverseNavigation != null,
                "navigationToTarget == null and inverseNavigation == null");

            Check.DebugAssert(
                targetIsPrincipal != null || required == null,
                "required should only be set if principal end is known");

            var navigationProperty = navigationToTarget?.MemberInfo;
            if (targetIsPrincipal == false
                || (inverseNavigation == null
                    && navigationProperty?.GetMemberType().IsAssignableFrom(
                        targetEntityType.ClrType) == false))
            {
                // Target is expected to be dependent or only one nav specified and it can't be the nav to principal
                return targetEntityType.Builder.HasRelationship(
                    Metadata, null, navigationToTarget, !targetIsPrincipal, configurationSource, required);
            }

            var existingRelationship = InternalForeignKeyBuilder.FindCurrentForeignKeyBuilder(
                targetEntityType,
                Metadata,
                navigationToTarget,
                inverseNavigation,
                dependentProperties: null,
                principalProperties: null);
            if (existingRelationship != null)
            {
                var shouldInvert = false;
                // The dependent and principal sides could be in the same hierarchy so we need to use the navigations to determine
                // the expected principal side.
                // And since both sides are in the same hierarchy different navigations must have different names.
                if (navigationToTarget != null)
                {
                    if (navigationToTarget.Value.Name == existingRelationship.Metadata.DependentToPrincipal?.Name)
                    {
                        existingRelationship.Metadata.UpdateDependentToPrincipalConfigurationSource(configurationSource);
                    }
                    else if (targetIsPrincipal == true)
                    {
                        shouldInvert = true;
                    }
                    else
                    {
                        existingRelationship.Metadata.UpdatePrincipalToDependentConfigurationSource(configurationSource);
                    }

                    if (navigationToTarget.Value.Name != null)
                    {
                        Metadata.RemoveIgnored(navigationToTarget.Value.Name);
                    }
                }

                if (inverseNavigation != null)
                {
                    if (inverseNavigation.Value.Name == existingRelationship.Metadata.PrincipalToDependent?.Name)
                    {
                        existingRelationship.Metadata.UpdatePrincipalToDependentConfigurationSource(configurationSource);
                    }
                    else if (targetIsPrincipal == true)
                    {
                        shouldInvert = true;
                    }
                    else
                    {
                        existingRelationship.Metadata.UpdateDependentToPrincipalConfigurationSource(configurationSource);
                    }

                    if (inverseNavigation.Value.Name != null)
                    {
                        targetEntityType.RemoveIgnored(inverseNavigation.Value.Name);
                    }
                }

                existingRelationship.Metadata.UpdateConfigurationSource(configurationSource);

                if (!shouldInvert)
                {
                    if (targetIsPrincipal == true)
                    {
                        existingRelationship = existingRelationship.HasEntityTypes(
                            existingRelationship.Metadata.PrincipalEntityType,
                            existingRelationship.Metadata.DeclaringEntityType,
                            configurationSource);

                        if (required.HasValue)
                        {
                            existingRelationship = existingRelationship.IsRequired(required.Value, configurationSource);
                        }
                    }

                    return existingRelationship;
                }

                // If relationship should be inverted it will be handled below
            }
            else
            {
                existingRelationship = InternalForeignKeyBuilder.FindCurrentForeignKeyBuilder(
                    Metadata,
                    targetEntityType,
                    inverseNavigation,
                    navigationToTarget,
                    dependentProperties: null,
                    principalProperties: null);
                if (existingRelationship != null)
                {
                    // Since the existing relationship didn't match the first case then the dependent and principal sides
                    // are not in the same hierarchy therefore we don't need to check existing navigations
                    if (navigationToTarget != null)
                    {
                        Check.DebugAssert(navigationToTarget.Value.Name == existingRelationship.Metadata.PrincipalToDependent?.Name,
                            $"Expected {navigationToTarget.Value.Name}, found {existingRelationship.Metadata.PrincipalToDependent?.Name}");

                        existingRelationship.Metadata.UpdatePrincipalToDependentConfigurationSource(configurationSource);
                        if (navigationToTarget.Value.Name != null)
                        {
                            Metadata.RemoveIgnored(navigationToTarget.Value.Name);
                        }
                    }

                    if (inverseNavigation != null)
                    {
                        Check.DebugAssert(inverseNavigation.Value.Name == existingRelationship.Metadata.DependentToPrincipal?.Name,
                            $"Expected {inverseNavigation.Value.Name}, found {existingRelationship.Metadata.DependentToPrincipal?.Name}");

                        existingRelationship.Metadata.UpdateDependentToPrincipalConfigurationSource(configurationSource);
                        if (inverseNavigation.Value.Name != null)
                        {
                            targetEntityType.RemoveIgnored(inverseNavigation.Value.Name);
                        }
                    }

                    existingRelationship.Metadata.UpdateConfigurationSource(configurationSource);

                    if (targetIsPrincipal == null)
                    {
                        return existingRelationship;
                    }
                }
            }

            InternalForeignKeyBuilder relationship;
            InternalForeignKeyBuilder newRelationship = null;
            using (var batcher = Metadata.Model.ConventionDispatcher.DelayConventions())
            {
                if (existingRelationship != null)
                {
                    relationship = existingRelationship;
                }
                else
                {
                    if (targetIsPrincipal == true
                        || targetEntityType.DefiningEntityType != Metadata)
                    {
                        newRelationship = CreateForeignKey(
                            targetEntityType.Builder,
                            dependentProperties: null,
                            principalKey: null,
                            navigationToPrincipalName: navigationProperty?.GetSimpleMemberName(),
                            required: required,
                            configurationSource: configurationSource);
                    }
                    else
                    {
                        var navigation = navigationToTarget;
                        navigationToTarget = inverseNavigation;
                        inverseNavigation = navigation;

                        navigationProperty = navigationToTarget?.MemberInfo;

                        newRelationship = targetEntityType.Builder.CreateForeignKey(
                            this,
                            dependentProperties: null,
                            principalKey: null,
                            navigationToPrincipalName: navigationProperty?.GetSimpleMemberName(),
                            required: null,
                            configurationSource: configurationSource);
                    }

                    relationship = newRelationship;
                }

                if (targetIsPrincipal == true)
                {
                    relationship = relationship
                        .HasEntityTypes(targetEntityType.Builder.Metadata, Metadata, configurationSource);

                    if (required.HasValue)
                    {
                        relationship = relationship.IsRequired(required.Value, configurationSource);
                    }
                }

                var inverseProperty = inverseNavigation?.MemberInfo;
                if (inverseNavigation == null)
                {
                    relationship = navigationProperty != null
                        ? relationship.HasNavigation(
                            navigationProperty,
                            pointsToPrincipal: true,
                            configurationSource)
                        : relationship.HasNavigation(
                            navigationToTarget.Value.Name,
                            pointsToPrincipal: true,
                            configurationSource);
                }
                else if (navigationToTarget == null)
                {
                    relationship = inverseProperty != null
                        ? relationship.HasNavigation(
                            inverseProperty,
                            pointsToPrincipal: false,
                            configurationSource)
                        : relationship.HasNavigation(
                            inverseNavigation.Value.Name,
                            pointsToPrincipal: false,
                            configurationSource);
                }
                else
                {
                    relationship = navigationProperty != null || inverseProperty != null
                        ? relationship.HasNavigations(navigationProperty, inverseProperty, configurationSource)
                        : relationship.HasNavigations(navigationToTarget.Value.Name, inverseNavigation.Value.Name, configurationSource);
                }

                if (relationship != null)
                {
                    relationship = batcher.Run(relationship);
                }
            }

            if (relationship != null
                && ((navigationToTarget != null
                        && relationship.Metadata.DependentToPrincipal?.Name != navigationToTarget.Value.Name)
                    || (inverseNavigation != null
                        && relationship.Metadata.PrincipalToDependent?.Name != inverseNavigation.Value.Name))
                && ((inverseNavigation != null
                        && relationship.Metadata.DependentToPrincipal?.Name != inverseNavigation.Value.Name)
                    || (navigationToTarget != null
                        && relationship.Metadata.PrincipalToDependent?.Name != navigationToTarget.Value.Name)))
            {
                relationship = null;
            }

            if (relationship == null)
            {
                if (newRelationship?.Metadata.Builder != null)
                {
                    newRelationship.Metadata.DeclaringEntityType.Builder.HasNoRelationship(newRelationship.Metadata, configurationSource);
                }

                return null;
            }

            return relationship;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalForeignKeyBuilder HasRelationship(
            [NotNull] EntityType principalEntityType,
            ConfigurationSource configurationSource)
            => HasRelationshipInternal(principalEntityType, principalKey: null, configurationSource: configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalForeignKeyBuilder HasRelationship(
            [NotNull] EntityType principalEntityType,
            [NotNull] Key principalKey,
            ConfigurationSource configurationSource)
            => HasRelationshipInternal(principalEntityType, principalKey, configurationSource);

        private InternalForeignKeyBuilder HasRelationshipInternal(
            EntityType targetEntityType,
            Key principalKey,
            ConfigurationSource configurationSource)
        {
            InternalForeignKeyBuilder relationship;
            InternalForeignKeyBuilder newRelationship;
            using (var batch = Metadata.Model.ConventionDispatcher.DelayConventions())
            {
                relationship = CreateForeignKey(
                    targetEntityType.Builder,
                    null,
                    principalKey,
                    null,
                    null,
                    configurationSource);

                newRelationship = relationship;
                if (principalKey != null)
                {
                    newRelationship = newRelationship.HasEntityTypes(targetEntityType, Metadata, configurationSource)
                        ?.HasPrincipalKey(principalKey.Properties, configurationSource);
                }

                newRelationship = newRelationship == null ? null : batch.Run(newRelationship);
            }

            if (newRelationship == null)
            {
                if (relationship?.Metadata.Builder != null)
                {
                    relationship.Metadata.DeclaringEntityType.Builder.HasNoRelationship(relationship.Metadata, configurationSource);
                }

                return null;
            }

            return newRelationship;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalForeignKeyBuilder HasOwnership(
            [NotNull] string targetEntityTypeName,
            [NotNull] string navigationName,
            ConfigurationSource configurationSource)
            => HasOwnership(
                new TypeIdentity(targetEntityTypeName), MemberIdentity.Create(navigationName),
                inverse: null, configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalForeignKeyBuilder HasOwnership(
            [NotNull] Type targetEntityType,
            [NotNull] string navigationName,
            ConfigurationSource configurationSource)
            => HasOwnership(
                new TypeIdentity(targetEntityType, Metadata.Model), MemberIdentity.Create(navigationName),
                inverse: null, configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalForeignKeyBuilder HasOwnership(
            [NotNull] Type targetEntityType,
            [NotNull] MemberInfo navigationMember,
            ConfigurationSource configurationSource)
            => HasOwnership(
                new TypeIdentity(targetEntityType, Metadata.Model), MemberIdentity.Create(navigationMember),
                inverse: null, configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalForeignKeyBuilder HasOwnership(
            [NotNull] Type targetEntityType,
            [NotNull] string navigationPropertyName,
            [CanBeNull] string inversePropertyName,
            ConfigurationSource configurationSource)
            => HasOwnership(
                new TypeIdentity(targetEntityType, Metadata.Model),
                MemberIdentity.Create(navigationPropertyName),
                MemberIdentity.Create(inversePropertyName),
                configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalForeignKeyBuilder HasOwnership(
            [NotNull] Type targetEntityType,
            [NotNull] MemberInfo navigationMember,
            [CanBeNull] MemberInfo inverseMember,
            ConfigurationSource configurationSource)
            => HasOwnership(
                new TypeIdentity(targetEntityType, Metadata.Model),
                MemberIdentity.Create(navigationMember),
                MemberIdentity.Create(inverseMember),
                configurationSource);

        private InternalForeignKeyBuilder HasOwnership(
            in TypeIdentity targetEntityType,
            MemberIdentity navigation,
            MemberIdentity? inverse,
            ConfigurationSource configurationSource)
        {
            InternalEntityTypeBuilder ownedEntityType;
            InternalForeignKeyBuilder relationship;
            using (var batch = Metadata.Model.ConventionDispatcher.DelayConventions())
            {
                var existingNavigation = Metadata.FindNavigation(navigation.Name);
                if (existingNavigation != null)
                {
                    if (existingNavigation.TargetEntityType.Name == targetEntityType.Name)
                    {
                        var existingOwnedEntityType = existingNavigation.ForeignKey.DeclaringEntityType;
                        if (existingOwnedEntityType.HasDefiningNavigation())
                        {
                            if (targetEntityType.Type != null)
                            {
                                ModelBuilder.Entity(
                                    targetEntityType.Type,
                                    existingOwnedEntityType.DefiningNavigationName,
                                    existingOwnedEntityType.DefiningEntityType,
                                    configurationSource);
                            }
                            else
                            {
                                ModelBuilder.Entity(
                                    targetEntityType.Name,
                                    existingOwnedEntityType.DefiningNavigationName,
                                    existingOwnedEntityType.DefiningEntityType,
                                    configurationSource);
                            }
                        }
                        else
                        {
                            if (targetEntityType.Type != null)
                            {
                                ModelBuilder.Entity(targetEntityType.Type, configurationSource, shouldBeOwned: true);
                            }
                            else
                            {
                                ModelBuilder.Entity(targetEntityType.Name, configurationSource, shouldBeOwned: true);
                            }
                        }

                        var ownershipBuilder = existingNavigation.ForeignKey.Builder;
                        ownershipBuilder = ownershipBuilder
                            .IsRequired(true, configurationSource)
                            ?.HasEntityTypes(
                                Metadata, ownershipBuilder.Metadata.FindNavigationsFromInHierarchy(Metadata).Single().TargetEntityType,
                                configurationSource)
                            ?.HasNavigations(inverse, navigation, configurationSource)
                            ?.IsOwnership(true, configurationSource);

                        return ownershipBuilder == null ? null : batch.Run(ownershipBuilder);
                    }

                    if (existingNavigation.ForeignKey.DeclaringEntityType.Builder
                            .HasNoRelationship(existingNavigation.ForeignKey, configurationSource)
                        == null)
                    {
                        return null;
                    }
                }

                var principalBuilder = this;
                var targetTypeName = targetEntityType.Name;
                var targetType = targetEntityType.Type;
                if (targetType == null)
                {
                    var memberType = existingNavigation?.GetIdentifyingMemberInfo()?.GetMemberType();
                    if (memberType != null)
                    {
                        targetType = memberType.TryGetSequenceType() ?? memberType;
                    }
                }

                ownedEntityType = targetType == null
                    ? ModelBuilder.Metadata.FindEntityType(targetTypeName)?.Builder
                    : ModelBuilder.Metadata.FindEntityType(targetType)?.Builder;
                if (ownedEntityType == null)
                {
                    if (Metadata.Model.EntityTypeShouldHaveDefiningNavigation(targetTypeName))
                    {
                        if (!configurationSource.Overrides(ConfigurationSource.Explicit)
                            && (targetType == null
                                ? Metadata.IsInDefinitionPath(targetTypeName)
                                : Metadata.IsInDefinitionPath(targetType)))
                        {
                            return null;
                        }

                        ownedEntityType = targetType == null
                            ? ModelBuilder.Entity(targetTypeName, navigation.Name, Metadata, configurationSource)
                            : ModelBuilder.Entity(targetType, navigation.Name, Metadata, configurationSource);
                    }
                    else
                    {
                        if (ModelBuilder.IsIgnored(targetTypeName, configurationSource))
                        {
                            return null;
                        }

                        ModelBuilder.Metadata.RemoveIgnored(targetTypeName);

                        ownedEntityType = targetType == null
                            ? ModelBuilder.Entity(targetTypeName, configurationSource, shouldBeOwned: true)
                            : ModelBuilder.Entity(targetType, configurationSource, shouldBeOwned: true);
                    }

                    if (ownedEntityType == null)
                    {
                        return null;
                    }
                }
                else
                {
                    var otherOwnership = ownedEntityType.Metadata.FindDeclaredOwnership();
                    if (otherOwnership != null)
                    {
                        if (!configurationSource.Overrides(ConfigurationSource.Explicit)
                            && (targetType == null
                                ? Metadata.IsInDefinitionPath(targetTypeName)
                                : Metadata.IsInDefinitionPath(targetType)))
                        {
                            return null;
                        }

                        var newOtherOwnership = otherOwnership.Builder.IsWeakTypeDefinition(configurationSource);
                        if (newOtherOwnership == null)
                        {
                            return null;
                        }

                        if (otherOwnership.DeclaringEntityType == Metadata)
                        {
                            principalBuilder = newOtherOwnership.Metadata.DeclaringEntityType.Builder;
                        }

                        ownedEntityType = targetType == null
                            ? ModelBuilder.Entity(targetTypeName, navigation.Name, principalBuilder.Metadata, configurationSource)
                            : ModelBuilder.Entity(targetType, navigation.Name, principalBuilder.Metadata, configurationSource);
                    }
                }

                relationship = ownedEntityType.HasRelationship(
                    targetEntityType: principalBuilder.Metadata,
                    navigationToTarget: inverse,
                    inverseNavigation: navigation,
                    targetIsPrincipal: true,
                    configurationSource: configurationSource,
                    required: true);
                relationship = batch.Run(relationship.IsOwnership(true, configurationSource));
            }

            if (relationship?.Metadata.Builder == null)
            {
                if (ownedEntityType.Metadata.Builder != null
                    && ownedEntityType.Metadata.HasDefiningNavigation())
                {
                    ModelBuilder.HasNoEntityType(ownedEntityType.Metadata, configurationSource);
                }

                return null;
            }

            return relationship;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool RemoveNonOwnershipRelationships([NotNull] ForeignKey ownership, ConfigurationSource configurationSource)
        {
            var incompatibleRelationships = Metadata.GetDerivedTypesInclusive()
                .SelectMany(t => t.GetDeclaredForeignKeys())
                .Where(
                    fk => !fk.IsOwnership
                        && fk.PrincipalToDependent != null
                        && !Contains(ownership, fk))
                .Concat(
                    Metadata.GetDerivedTypesInclusive()
                        .SelectMany(t => t.GetDeclaredReferencingForeignKeys())
                        .Where(
                            fk => !fk.IsOwnership
                                && !Contains(fk.DeclaringEntityType.FindOwnership(), fk)))
                .ToList();

            if (incompatibleRelationships.Any(fk => !configurationSource.Overrides(fk.GetConfigurationSource())))
            {
                return false;
            }

            foreach (var foreignKey in incompatibleRelationships)
            {
                // foreignKey.Builder can be null below if calling HasNoRelationship() below
                // affects the other foreign key(s) in incompatibleRelationships
                if (foreignKey.Builder != null)
                {
                    foreignKey.DeclaringEntityType.Builder.HasNoRelationship(foreignKey, configurationSource);
                }
            }

            return true;
        }

        private bool Contains(IForeignKey inheritedFk, IForeignKey derivedFk)
            => inheritedFk != null
                && inheritedFk.PrincipalEntityType.IsAssignableFrom(derivedFk.PrincipalEntityType)
                && PropertyListComparer.Instance.Equals(inheritedFk.Properties, derivedFk.Properties);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalEntityTypeBuilder GetTargetEntityTypeBuilder(
            [NotNull] Type targetClrType,
            [NotNull] MemberInfo navigationInfo,
            ConfigurationSource? configurationSource)
        {
            var ownership = Metadata.FindOwnership();

            // ReSharper disable CheckForReferenceEqualityInstead.1
            // ReSharper disable CheckForReferenceEqualityInstead.3
            if (ownership != null)
            {
                if (targetClrType.Equals(Metadata.ClrType))
                {
                    return null;
                }

                if (targetClrType.IsAssignableFrom(ownership.PrincipalEntityType.ClrType))
                {
                    if (configurationSource != null)
                    {
                        ownership.PrincipalEntityType.UpdateConfigurationSource(configurationSource.Value);
                    }

                    return ownership.PrincipalEntityType.Builder;
                }
            }

            var entityType = Metadata;
            InternalEntityTypeBuilder targetEntityTypeBuilder = null;
            if (!ModelBuilder.Metadata.EntityTypeShouldHaveDefiningNavigation(targetClrType))
            {
                var targetEntityType = ModelBuilder.Metadata.FindEntityType(targetClrType);

                var existingOwnership = targetEntityType?.FindOwnership();
                if (existingOwnership != null
                    && entityType.Model.IsOwned(targetClrType)
                    && (existingOwnership.PrincipalEntityType != entityType
                        || existingOwnership.PrincipalToDependent?.Name != navigationInfo.GetSimpleMemberName()))
                {
                    return configurationSource.HasValue
                        && !targetClrType.Equals(Metadata.ClrType)
                            ? ModelBuilder.Entity(
                                targetClrType, navigationInfo.GetSimpleMemberName(), entityType, configurationSource.Value)
                            : null;
                }

                var owned = existingOwnership != null
                    || entityType.Model.IsOwned(targetClrType);
                targetEntityTypeBuilder = configurationSource.HasValue
                    ? ModelBuilder.Entity(targetClrType, configurationSource.Value, owned)
                    : targetEntityType?.Builder;
            }
            else if (!targetClrType.Equals(Metadata.ClrType))
            {
                if (entityType.DefiningEntityType?.ClrType.Equals(targetClrType) == true)
                {
                    if (configurationSource != null)
                    {
                        entityType.DefiningEntityType.UpdateConfigurationSource(configurationSource.Value);
                    }

                    return entityType.DefiningEntityType.Builder;
                }

                targetEntityTypeBuilder =
                    entityType.FindNavigation(navigationInfo.GetSimpleMemberName())?.TargetEntityType.Builder
                    ?? entityType.Model.FindEntityType(
                        targetClrType, navigationInfo.GetSimpleMemberName(), entityType)?.Builder;

                if (targetEntityTypeBuilder == null
                    && configurationSource.HasValue
                    && !entityType.IsInDefinitionPath(targetClrType)
                    && !entityType.IsInOwnershipPath(targetClrType))
                {
                    return ModelBuilder.Entity(
                        targetClrType, navigationInfo.GetSimpleMemberName(), entityType, configurationSource.Value);
                }

                if (configurationSource != null)
                {
                    targetEntityTypeBuilder?.Metadata.UpdateConfigurationSource(configurationSource.Value);
                }
            }
            // ReSharper restore CheckForReferenceEqualityInstead.1
            // ReSharper restore CheckForReferenceEqualityInstead.3

            return targetEntityTypeBuilder;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalForeignKeyBuilder CreateForeignKey(
            [NotNull] InternalEntityTypeBuilder principalEntityTypeBuilder,
            [CanBeNull] IReadOnlyList<Property> dependentProperties,
            [CanBeNull] Key principalKey,
            [CanBeNull] string navigationToPrincipalName,
            bool? required,
            ConfigurationSource configurationSource)
        {
            using var batch = ModelBuilder.Metadata.ConventionDispatcher.DelayConventions();
            var foreignKey = SetOrAddForeignKey(
                null, principalEntityTypeBuilder,
                dependentProperties, principalKey, navigationToPrincipalName, required, configurationSource);

            if (required.HasValue
                && foreignKey?.IsRequired == required.Value)
            {
                foreignKey.SetIsRequired(required.Value, configurationSource);
            }

            return (InternalForeignKeyBuilder)batch.Run(foreignKey)?.Builder;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalForeignKeyBuilder UpdateForeignKey(
            [NotNull] ForeignKey foreignKey,
            [CanBeNull] IReadOnlyList<Property> dependentProperties,
            [CanBeNull] Key principalKey,
            [CanBeNull] string navigationToPrincipalName,
            bool? isRequired,
            ConfigurationSource? configurationSource)
        {
            using var batch = ModelBuilder.Metadata.ConventionDispatcher.DelayConventions();
            foreignKey = SetOrAddForeignKey(
                foreignKey, foreignKey.PrincipalEntityType.Builder,
                dependentProperties, principalKey, navigationToPrincipalName, isRequired, configurationSource);

            return (InternalForeignKeyBuilder)batch.Run(foreignKey)?.Builder;
        }

        private ForeignKey SetOrAddForeignKey(
            ForeignKey foreignKey,
            InternalEntityTypeBuilder principalEntityTypeBuilder,
            IReadOnlyList<Property> dependentProperties,
            Key principalKey,
            string navigationToPrincipalName,
            bool? isRequired,
            ConfigurationSource? configurationSource)
        {
            var principalType = principalEntityTypeBuilder.Metadata;
            var principalBaseEntityTypeBuilder = principalType.RootType().Builder;
            if (principalKey == null)
            {
                if (principalType.IsKeyless
                    && !configurationSource.Overrides(principalType.GetIsKeylessConfigurationSource()))
                {
                    return null;
                }

                principalKey = principalType.FindPrimaryKey();
                if (principalKey != null
                    && dependentProperties != null
                    && (!ForeignKey.AreCompatible(
                            principalKey.Properties,
                            dependentProperties,
                            principalType,
                            Metadata,
                            shouldThrow: false)
                        || (foreignKey == null
                            && Metadata.FindForeignKeysInHierarchy(dependentProperties, principalKey, principalType).Any())))
                {
                    principalKey = null;
                }

                if (principalKey == null
                    && foreignKey != null
                    && (dependentProperties == null
                        || ForeignKey.AreCompatible(
                            foreignKey.PrincipalKey.Properties,
                            dependentProperties,
                            principalType,
                            Metadata,
                            shouldThrow: false)))
                {
                    principalKey = foreignKey.PrincipalKey;
                }
            }

            if (dependentProperties != null)
            {
                dependentProperties = GetActualProperties(dependentProperties, ConfigurationSource.Convention);
                if (principalKey == null)
                {
                    var principalKeyProperties = principalBaseEntityTypeBuilder.TryCreateUniqueProperties(
                        dependentProperties.Count, null, Enumerable.Repeat("", dependentProperties.Count),
                        dependentProperties.Select(p => p.ClrType), isRequired: true, baseName: "TempId").Item2;

                    principalKey = principalBaseEntityTypeBuilder.HasKeyInternal(principalKeyProperties, ConfigurationSource.Convention)
                        .Metadata;
                }
                else
                {
                    Check.DebugAssert(
                        foreignKey != null
                        || Metadata.FindForeignKey(dependentProperties, principalKey, principalType) == null,
                        "FK not found");
                }
            }
            else
            {
                if (principalKey == null)
                {
                    var principalKeyProperties = principalBaseEntityTypeBuilder.TryCreateUniqueProperties(
                        1, null, new[] { "TempId" }, new[] { typeof(int) }, isRequired: true, baseName: "").Item2;

                    principalKey = principalBaseEntityTypeBuilder.HasKeyInternal(
                        principalKeyProperties, ConfigurationSource.Convention).Metadata;
                }

                if (foreignKey != null)
                {
                    var oldProperties = foreignKey.Properties;
                    var oldKey = foreignKey.PrincipalKey;
                    var temporaryProperties = CreateUniqueProperties(null, principalKey.Properties, isRequired ?? false, "TempFk");
                    foreignKey.SetProperties(temporaryProperties, principalKey, configurationSource);

                    foreignKey.DeclaringEntityType.Builder.RemoveUnusedShadowProperties(oldProperties);
                    if (oldKey != principalKey)
                    {
                        oldKey.DeclaringEntityType.Builder.RemoveKeyIfUnused(oldKey);
                    }
                }

                var baseName = string.IsNullOrEmpty(navigationToPrincipalName)
                    ? principalType.ShortName()
                    : navigationToPrincipalName;
                dependentProperties = CreateUniqueProperties(null, principalKey.Properties, isRequired ?? false, baseName);
            }

            if (foreignKey == null)
            {
                return Metadata.AddForeignKey(
                    dependentProperties, principalKey, principalType, componentConfigurationSource: null, configurationSource.Value);
            }

            var oldFKProperties = foreignKey.Properties;
            var oldPrincipalKey = foreignKey.PrincipalKey;
            foreignKey.SetProperties(dependentProperties, principalKey, configurationSource);

            if (oldFKProperties != dependentProperties)
            {
                foreignKey.DeclaringEntityType.Builder.RemoveUnusedShadowProperties(oldFKProperties);
            }

            if (oldPrincipalKey != principalKey)
            {
                oldPrincipalKey.DeclaringEntityType.Builder.RemoveKeyIfUnused(oldPrincipalKey);
            }

            return foreignKey;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalSkipNavigationBuilder HasSkipNavigation(
            MemberIdentity navigationProperty,
            [NotNull] EntityType targetEntityType,
            ConfigurationSource? configurationSource,
            bool collection = true,
            bool onDependent = false)
        {
            List<SkipNavigation> navigationsToDetach = null;
            List<(InternalSkipNavigationBuilder Navigation, InternalSkipNavigationBuilder Inverse)> detachedNavigations = null;
            var navigationName = navigationProperty.Name;
            var memberInfo = navigationProperty.MemberInfo;
            var existingNavigation = Metadata.FindSkipNavigation(navigationName);
            if (existingNavigation != null)
            {
                Check.DebugAssert(memberInfo == null || memberInfo.IsSameAs(existingNavigation.GetIdentifyingMemberInfo()),
                    "Expected memberInfo to be the same on the existing navigation");

                Check.DebugAssert(collection == existingNavigation.IsCollection,
                    "Only collection skip navigations are currently supported");

                Check.DebugAssert(onDependent == existingNavigation.IsOnDependent,
                    "Only skip navigations on principals are currently supported");

                if (existingNavigation.DeclaringEntityType != Metadata)
                {
                    if (!IsIgnored(navigationName, configurationSource))
                    {
                        Metadata.RemoveIgnored(navigationName);
                    }
                }

                if (configurationSource.HasValue)
                {
                    existingNavigation.UpdateConfigurationSource(configurationSource.Value);
                }

                return existingNavigation.Builder;
            }
            else
            {
                if (!configurationSource.HasValue)
                {
                    return null;
                }

                if (IsIgnored(navigationName, configurationSource))
                {
                    return null;
                }

                foreach (var conflictingMember in Metadata.FindPropertiesInHierarchy(navigationName).Cast<PropertyBase>()
                    .Concat(Metadata.FindNavigationsInHierarchy(navigationName))
                    .Concat(Metadata.FindServicePropertiesInHierarchy(navigationName)))
                {
                    if (!configurationSource.Overrides(conflictingMember.GetConfigurationSource()))
                    {
                        return null;
                    }
                }

                foreach (var derivedType in Metadata.GetDerivedTypes())
                {
                    var conflictingNavigation = derivedType.FindDeclaredSkipNavigation(navigationName);
                    if (conflictingNavigation != null)
                    {
                        if (navigationsToDetach == null)
                        {
                            navigationsToDetach = new List<SkipNavigation>();
                        }

                        navigationsToDetach.Add(conflictingNavigation);
                    }
                }
            }

            InternalSkipNavigationBuilder builder;
            using (ModelBuilder.Metadata.ConventionDispatcher.DelayConventions())
            {
                Metadata.RemoveIgnored(navigationName);

                foreach (var conflictingProperty in Metadata.FindPropertiesInHierarchy(navigationName))
                {
                    if (conflictingProperty.GetConfigurationSource() != ConfigurationSource.Explicit)
                    {
                        conflictingProperty.DeclaringEntityType.RemoveProperty(conflictingProperty);
                    }
                }

                foreach (var conflictingServiceProperty in Metadata.FindServicePropertiesInHierarchy(navigationName))
                {
                    if (conflictingServiceProperty.GetConfigurationSource() != ConfigurationSource.Explicit)
                    {
                        conflictingServiceProperty.DeclaringEntityType.RemoveServiceProperty(conflictingServiceProperty);
                    }
                }

                foreach (var conflictingNavigation in Metadata.FindNavigationsInHierarchy(navigationName))
                {
                    if (conflictingNavigation.GetConfigurationSource() == ConfigurationSource.Explicit)
                    {
                        continue;
                    }

                    var conflictingForeignKey = conflictingNavigation.ForeignKey;
                    if (conflictingForeignKey.GetConfigurationSource() == ConfigurationSource.Convention)
                    {
                        conflictingForeignKey.DeclaringEntityType.Builder.HasNoRelationship(conflictingForeignKey, ConfigurationSource.Convention);
                    }
                    else if (conflictingForeignKey.Builder.HasNavigation(
                            (string)null,
                            conflictingNavigation.IsOnDependent,
                            configurationSource.Value) == null)
                    {
                        return null;
                    }
                }

                if (navigationsToDetach != null)
                {
                    detachedNavigations = new List<(InternalSkipNavigationBuilder, InternalSkipNavigationBuilder)>();
                    foreach (var navigationToDetach in navigationsToDetach)
                    {
                        var inverse = navigationToDetach.Inverse;
                        detachedNavigations.Add((DetachSkipNavigation(navigationToDetach), DetachSkipNavigation(inverse)));
                    }
                }

                builder = Metadata.AddSkipNavigation(
                    navigationName, navigationProperty.MemberInfo,
                    targetEntityType, collection, onDependent, configurationSource.Value).Builder;

                if (detachedNavigations != null)
                {
                    foreach (var detachedSkipNavigationTuple in detachedNavigations)
                    {
                        detachedSkipNavigationTuple.Navigation.Attach(this, inverseBuilder: detachedSkipNavigationTuple.Inverse);
                    }
                }
            }

            return builder.Metadata.Builder == null
                    ? Metadata.FindSkipNavigation(navigationName)?.Builder
                    : builder;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalEntityTypeBuilder HasNoSkipNavigation(
            [NotNull] SkipNavigation skipNavigation, ConfigurationSource configurationSource)
        {
            var currentConfigurationSource = skipNavigation.GetConfigurationSource();
            if (!configurationSource.Overrides(currentConfigurationSource))
            {
                return null;
            }

            if (skipNavigation.Inverse != null)
            {
                var removed = skipNavigation.Inverse.Builder.HasInverse(null, configurationSource);
                Check.DebugAssert(removed != null, "Expected inverse to be removed");
            }

            Metadata.RemoveSkipNavigation(skipNavigation);

            return this;
        }

        private static InternalSkipNavigationBuilder DetachSkipNavigation(SkipNavigation skipNavigationToDetach)
        {
            var builder = skipNavigationToDetach?.Builder;
            if (builder == null)
            {
                return null;
            }

            skipNavigationToDetach.DeclaringEntityType.RemoveSkipNavigation(skipNavigationToDetach);
            return builder;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool ShouldReuniquifyTemporaryProperties([NotNull] ForeignKey foreignKey)
            => TryCreateUniqueProperties(
                    foreignKey.PrincipalKey.Properties.Count,
                    foreignKey.Properties,
                    foreignKey.PrincipalKey.Properties.Select(p => p.Name),
                    foreignKey.PrincipalKey.Properties.Select(p => p.ClrType),
                    foreignKey.IsRequired
                    && foreignKey.GetIsRequiredConfigurationSource().Overrides(ConfigurationSource.Convention),
                    foreignKey.DependentToPrincipal?.Name ?? foreignKey.PrincipalEntityType.ShortName())
                .Item1;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual Property CreateUniqueProperty(
            [NotNull] string propertyName,
            [NotNull] Type propertyType,
            bool isRequired)
            => CreateUniqueProperties(
                new[] { propertyName },
                new[] { propertyType },
                isRequired).First();

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual IReadOnlyList<Property> CreateUniqueProperties(
            [NotNull] IReadOnlyList<string> propertyNames,
            [NotNull] IReadOnlyList<Type> propertyTypes,
            bool isRequired)
            => TryCreateUniqueProperties(
                propertyNames.Count,
                null,
                propertyNames,
                propertyTypes,
                isRequired,
                "").Item2;

        private IReadOnlyList<Property> CreateUniqueProperties(
            IReadOnlyList<Property> currentProperties,
            IReadOnlyList<Property> principalProperties,
            bool isRequired,
            string baseName)
            => TryCreateUniqueProperties(
                principalProperties.Count,
                currentProperties,
                principalProperties.Select(p => p.Name),
                principalProperties.Select(p => p.ClrType),
                isRequired,
                baseName).Item2;

        private (bool, IReadOnlyList<Property>) TryCreateUniqueProperties(
            int propertyCount,
            IReadOnlyList<Property> currentProperties,
            IEnumerable<string> principalPropertyNames,
            IEnumerable<Type> principalPropertyTypes,
            bool isRequired,
            string baseName)
        {
            var newProperties = currentProperties == null ? new Property[propertyCount] : null;
            var clrProperties = Metadata.GetRuntimeProperties();
            var clrFields = Metadata.GetRuntimeFields();
            var canReuniquify = false;
            using (var principalPropertyNamesEnumerator = principalPropertyNames.GetEnumerator())
            {
                using var principalPropertyTypesEnumerator = principalPropertyTypes.GetEnumerator();
                for (var i = 0;
                     i < propertyCount
                     && principalPropertyNamesEnumerator.MoveNext()
                     && principalPropertyTypesEnumerator.MoveNext();
                     i++)
                {
                    var keyPropertyName = principalPropertyNamesEnumerator.Current;
                    var keyPropertyType = principalPropertyTypesEnumerator.Current;
                    var keyModifiedBaseName = keyPropertyName.StartsWith(baseName, StringComparison.OrdinalIgnoreCase)
                        ? keyPropertyName
                        : baseName + keyPropertyName;
                    string propertyName;
                    var clrType = keyPropertyType.MakeNullable(!isRequired);
                    var index = -1;
                    while (true)
                    {
                        propertyName = keyModifiedBaseName + (++index > 0 ? index.ToString(CultureInfo.InvariantCulture) : "");
                        if (!Metadata.FindPropertiesInHierarchy(propertyName).Any()
                            && clrProperties?.ContainsKey(propertyName) != true
                            && clrFields?.ContainsKey(propertyName) != true
                            && !IsIgnored(propertyName, ConfigurationSource.Convention))
                        {
                            if (currentProperties == null)
                            {
                                var propertyBuilder = Property(
                                    clrType, propertyName, typeConfigurationSource: null,
                                    configurationSource: ConfigurationSource.Convention);

                                if (clrType.IsNullableType())
                                {
                                    propertyBuilder.IsRequired(isRequired, ConfigurationSource.Convention);
                                }

                                newProperties[i] = propertyBuilder.Metadata;
                            }
                            else
                            {
                                canReuniquify = true;
                            }

                            break;
                        }

                        var currentProperty = currentProperties?.SingleOrDefault(p => p.Name == propertyName);
                        if (currentProperty != null)
                        {
                            if (currentProperty.IsShadowProperty()
                                && currentProperty.ClrType != clrType
                                && isRequired)
                            {
                                canReuniquify = true;
                            }

                            break;
                        }
                    }
                }
            }

            return (canReuniquify, newProperties);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual IReadOnlyList<Property> GetOrCreateProperties(
            [CanBeNull] IReadOnlyList<string> propertyNames,
            ConfigurationSource? configurationSource,
            [CanBeNull] IReadOnlyList<Property> referencedProperties = null,
            bool required = false,
            bool useDefaultType = false)
        {
            if (propertyNames == null)
            {
                return null;
            }

            if (referencedProperties != null
                && referencedProperties.Count != propertyNames.Count)
            {
                referencedProperties = null;
            }

            var propertyList = new List<Property>();
            for (var i = 0; i < propertyNames.Count; i++)
            {
                var propertyName = propertyNames[i];
                var property = Metadata.FindProperty(propertyName);
                if (property == null)
                {
                    var type = referencedProperties == null
                        ? useDefaultType
                            ? typeof(int)
                            : null
                        : referencedProperties[i].ClrType;

                    if (!configurationSource.HasValue)
                    {
                        return null;
                    }

                    // TODO: Log that a shadow property is created
                    var propertyBuilder = Property(
                        required
                            ? type
                            : type?.MakeNullable(),
                        propertyName,
                        typeConfigurationSource: null,
                        configurationSource.Value);

                    if (propertyBuilder == null)
                    {
                        return null;
                    }

                    property = propertyBuilder.Metadata;
                }
                else if (configurationSource.HasValue)
                {
                    if (ConfigurationSource.Convention.Overrides(property.GetTypeConfigurationSource())
                        && property.IsShadowProperty()
                        && (!property.IsNullable || (required && property.GetIsNullableConfigurationSource() == null))
                        && property.ClrType.IsNullableType())
                    {
                        property = property.DeclaringEntityType.Builder.Property(
                                property.ClrType.MakeNullable(false),
                                property.Name,
                                configurationSource.Value)
                            .Metadata;
                    }
                    else
                    {
                        property = property.DeclaringEntityType.Builder.Property(property.Name, configurationSource.Value).Metadata;
                    }
                }

                propertyList.Add(property);
            }

            return propertyList;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual IReadOnlyList<Property> GetOrCreateProperties(
            [CanBeNull] IEnumerable<MemberInfo> clrMembers, ConfigurationSource? configurationSource)
        {
            if (clrMembers == null)
            {
                return null;
            }

            var list = new List<Property>();
            foreach (var propertyInfo in clrMembers)
            {
                var propertyBuilder = Property(propertyInfo, configurationSource);
                if (propertyBuilder == null)
                {
                    return null;
                }

                list.Add(propertyBuilder.Metadata);
            }

            return list;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual IReadOnlyList<Property> GetActualProperties(
            [CanBeNull] IReadOnlyList<Property> properties, ConfigurationSource? configurationSource)
        {
            if (properties == null)
            {
                return null;
            }

            var actualProperties = new Property[properties.Count];
            for (var i = 0; i < actualProperties.Length; i++)
            {
                var property = properties[i];
                var typeConfigurationSource = property.GetTypeConfigurationSource();
                var builder = property.Builder != null && property.DeclaringEntityType.IsAssignableFrom(Metadata)
                    ? property.Builder
                    : Property(
                        typeConfigurationSource.Overrides(ConfigurationSource.DataAnnotation) ? property.ClrType : null,
                        property.Name,
                        property.GetIdentifyingMemberInfo(),
                        typeConfigurationSource.Overrides(ConfigurationSource.DataAnnotation) ? typeConfigurationSource : null,
                        configurationSource);
                if (builder == null)
                {
                    return null;
                }

                actualProperties[i] = builder.Metadata;
            }

            return actualProperties;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalEntityTypeBuilder HasChangeTrackingStrategy(
            ChangeTrackingStrategy? changeTrackingStrategy, ConfigurationSource configurationSource)
        {
            if (CanSetChangeTrackingStrategy(changeTrackingStrategy, configurationSource))
            {
                Metadata.SetChangeTrackingStrategy(changeTrackingStrategy, configurationSource);

                return this;
            }

            return null;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool CanSetChangeTrackingStrategy(
            ChangeTrackingStrategy? changeTrackingStrategy, ConfigurationSource configurationSource)
            => configurationSource.Overrides(Metadata.GetChangeTrackingStrategyConfigurationSource())
                || Metadata.GetChangeTrackingStrategy() == changeTrackingStrategy;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalEntityTypeBuilder UsePropertyAccessMode(
            PropertyAccessMode? propertyAccessMode, ConfigurationSource configurationSource)
        {
            if (CanSetPropertyAccessMode(propertyAccessMode, configurationSource))
            {
                Metadata.SetPropertyAccessMode(propertyAccessMode, configurationSource);

                return this;
            }

            return null;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool CanSetPropertyAccessMode(PropertyAccessMode? propertyAccessMode, ConfigurationSource configurationSource)
            => configurationSource.Overrides(Metadata.GetPropertyAccessModeConfigurationSource())
                || Metadata.GetPropertyAccessMode() == propertyAccessMode;

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual DiscriminatorBuilder HasDiscriminator(ConfigurationSource configurationSource)
            => DiscriminatorBuilder(
                GetOrCreateDiscriminatorProperty(type: null, name: null, ConfigurationSource.Convention),
                configurationSource);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual DiscriminatorBuilder HasDiscriminator(
            [CanBeNull] string name, [CanBeNull] Type type, ConfigurationSource configurationSource)
        {
            Check.DebugAssert(name != null || type != null, $"Either {nameof(name)} or {nameof(type)} should be non-null");

            return CanSetDiscriminator(name, type, configurationSource)
                        ? DiscriminatorBuilder(
                            GetOrCreateDiscriminatorProperty(type, name, configurationSource),
                            configurationSource)
                        : null;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual DiscriminatorBuilder HasDiscriminator([NotNull] MemberInfo memberInfo, ConfigurationSource configurationSource)
            => CanSetDiscriminator(
                Check.NotNull(memberInfo, nameof(memberInfo)).GetSimpleMemberName(), memberInfo.GetMemberType(), configurationSource)
                ? DiscriminatorBuilder(
                    Metadata.RootType().Builder.Property(
                        memberInfo, configurationSource),
                    configurationSource)
                : null;

        private static readonly string _defaultDiscriminatorName = "Discriminator";

        private static readonly Type _defaultDiscriminatorType = typeof(string);

        private InternalPropertyBuilder GetOrCreateDiscriminatorProperty(Type type, string name, ConfigurationSource configurationSource)
        {
            var discriminatorProperty = ((IEntityType)Metadata).GetDiscriminatorProperty();
            if ((name != null && discriminatorProperty?.Name != name)
                || (type != null && discriminatorProperty?.ClrType != type))
            {
                discriminatorProperty = null;
            }

            return Metadata.RootType().Builder.Property(
                type ?? discriminatorProperty?.ClrType ?? _defaultDiscriminatorType,
                name ?? discriminatorProperty?.Name ?? _defaultDiscriminatorName,
                typeConfigurationSource: type != null ? configurationSource : (ConfigurationSource?)null,
                configurationSource: configurationSource);
        }

        private DiscriminatorBuilder DiscriminatorBuilder(
            [CanBeNull] InternalPropertyBuilder discriminatorPropertyBuilder,
            ConfigurationSource configurationSource)
        {
            if (discriminatorPropertyBuilder == null)
            {
                return null;
            }

            var rootTypeBuilder = Metadata.RootType().Builder;
            var discriminatorProperty = discriminatorPropertyBuilder.Metadata;
            // Make sure the property is on the root type
            discriminatorPropertyBuilder = rootTypeBuilder.Property(
                discriminatorProperty.ClrType, discriminatorProperty.Name, null, ConfigurationSource.Convention);

            RemoveUnusedDiscriminatorProperty(discriminatorProperty, configurationSource);

            rootTypeBuilder.Metadata.SetDiscriminatorProperty(discriminatorProperty, configurationSource);
            discriminatorPropertyBuilder.IsRequired(true, ConfigurationSource.Convention);
            discriminatorPropertyBuilder.HasValueGenerator(DiscriminatorValueGenerator.Factory, ConfigurationSource.Convention);

            return new DiscriminatorBuilder(Metadata);
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual InternalEntityTypeBuilder HasNoDiscriminator(ConfigurationSource configurationSource)
        {
            if (Metadata[CoreAnnotationNames.DiscriminatorProperty] != null
                && !configurationSource.Overrides(Metadata.GetDiscriminatorPropertyConfigurationSource()))
            {
                return null;
            }

            if (Metadata.BaseType == null)
            {
                RemoveUnusedDiscriminatorProperty(null, configurationSource);
            }

            Metadata.SetDiscriminatorProperty(null, configurationSource);

            if (configurationSource == ConfigurationSource.Explicit)
            {
                Metadata.SetDiscriminatorMappingComplete(null);
            }
            else if (CanSetAnnotation(CoreAnnotationNames.DiscriminatorMappingComplete, null, configurationSource))
            {
                Metadata.SetDiscriminatorMappingComplete(null, configurationSource == ConfigurationSource.DataAnnotation);
            }

            return this;
        }

        private void RemoveUnusedDiscriminatorProperty(Property newDiscriminatorProperty, ConfigurationSource configurationSource)
        {
            var oldDiscriminatorProperty = ((IEntityType)Metadata).GetDiscriminatorProperty() as Property;
            if (oldDiscriminatorProperty?.Builder != null
                && oldDiscriminatorProperty != newDiscriminatorProperty)
            {
                oldDiscriminatorProperty.DeclaringEntityType.Builder.RemoveUnusedShadowProperties(
                    new[] { oldDiscriminatorProperty });

                if (oldDiscriminatorProperty.Builder != null)
                {
                    oldDiscriminatorProperty.Builder.IsRequired(null, configurationSource);
                    oldDiscriminatorProperty.Builder.HasValueGenerator((Type)null, configurationSource);
                }
            }
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public virtual bool CanSetDiscriminator([CanBeNull] string name, [CanBeNull] Type type, ConfigurationSource configurationSource)
            => name == null && type == null
                ? CanRemoveDiscriminator(configurationSource)
                : CanSetDiscriminator(((IEntityType)Metadata).GetDiscriminatorProperty(), name, type, configurationSource);

        private bool CanSetDiscriminator(
            IProperty discriminatorProperty,
            string name,
            Type discriminatorType,
            ConfigurationSource configurationSource)
            => ((name == null && discriminatorType == null)
                    || ((name == null || discriminatorProperty?.Name == name)
                        && (discriminatorType == null || discriminatorProperty?.ClrType == discriminatorType))
                    || configurationSource.Overrides(Metadata.GetDiscriminatorPropertyConfigurationSource()))
                && (discriminatorProperty != null
                    || Metadata.RootType().Builder.CanAddDiscriminatorProperty(
                        discriminatorType ?? _defaultDiscriminatorType,
                        name ?? _defaultDiscriminatorName,
                        typeConfigurationSource: discriminatorType != null
                            ? configurationSource
                            : (ConfigurationSource?)null));

        private bool CanRemoveDiscriminator(ConfigurationSource configurationSource)
            => CanSetAnnotation(CoreAnnotationNames.DiscriminatorProperty, null, configurationSource);

        private bool CanAddDiscriminatorProperty(
            [NotNull] Type propertyType, [NotNull] string name, ConfigurationSource? typeConfigurationSource)
        {
            var conflictingProperty = Metadata.FindPropertiesInHierarchy(name).FirstOrDefault();
            if (conflictingProperty != null
                && conflictingProperty.IsShadowProperty()
                && conflictingProperty.ClrType != propertyType
                && typeConfigurationSource != null
                && !typeConfigurationSource.Overrides(conflictingProperty.GetTypeConfigurationSource()))
            {
                return false;
            }

            if (Metadata.ClrType == null)
            {
                return true;
            }

            var memberInfo = Metadata.ClrType.GetMembersInHierarchy(name).FirstOrDefault();
            if (memberInfo != null
                && propertyType != memberInfo.GetMemberType()
                && typeConfigurationSource != null)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        IConventionEntityType IConventionEntityTypeBuilder.Metadata
        {
            [DebuggerStepThrough] get => Metadata;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionEntityTypeBuilder IConventionEntityTypeBuilder.HasBaseType(IConventionEntityType baseEntityType, bool fromDataAnnotation)
            => HasBaseType(
                (EntityType)baseEntityType, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        bool IConventionEntityTypeBuilder.CanSetBaseType(IConventionEntityType baseEntityType, bool fromDataAnnotation)
            => CanSetBaseType(
                (EntityType)baseEntityType, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionPropertyBuilder IConventionEntityTypeBuilder.Property(
            Type propertyType, string propertyName, bool setTypeConfigurationSource, bool fromDataAnnotation)
            => Property(
                propertyType,
                propertyName, setTypeConfigurationSource
                    ? fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention
                    : (ConfigurationSource?)null, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionPropertyBuilder IConventionEntityTypeBuilder.Property(MemberInfo memberInfo, bool fromDataAnnotation)
            => Property(memberInfo, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IReadOnlyList<IConventionProperty> IConventionEntityTypeBuilder.GetOrCreateProperties(
            IReadOnlyList<string> propertyNames, bool fromDataAnnotation)
            => GetOrCreateProperties(
                propertyNames, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IReadOnlyList<IConventionProperty> IConventionEntityTypeBuilder.GetOrCreateProperties(
            IEnumerable<MemberInfo> memberInfos, bool fromDataAnnotation)
            => GetOrCreateProperties(memberInfos, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionEntityTypeBuilder IConventionEntityTypeBuilder.HasNoUnusedShadowProperties(
            IReadOnlyList<IConventionProperty> properties, bool fromDataAnnotation)
            => RemoveUnusedShadowProperties(
                properties, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionServicePropertyBuilder IConventionEntityTypeBuilder.ServiceProperty(MemberInfo memberInfo, bool fromDataAnnotation)
            => ServiceProperty(memberInfo, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        bool IConventionEntityTypeBuilder.IsIgnored(string name, bool fromDataAnnotation)
            => IsIgnored(name, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionEntityTypeBuilder IConventionEntityTypeBuilder.Ignore(string name, bool fromDataAnnotation)
            => Ignore(name, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        bool IConventionEntityTypeBuilder.CanIgnore(string name, bool fromDataAnnotation)
            => CanIgnore(name, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionKeyBuilder IConventionEntityTypeBuilder.PrimaryKey(
            IReadOnlyList<IConventionProperty> properties, bool fromDataAnnotation)
            => PrimaryKey(
                properties as IReadOnlyList<Property> ?? properties?.Cast<Property>().ToList(),
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        bool IConventionEntityTypeBuilder.CanSetPrimaryKey(IReadOnlyList<IConventionProperty> properties, bool fromDataAnnotation)
            => CanSetPrimaryKey(
                properties as IReadOnlyList<Property> ?? properties?.Cast<Property>().ToList(),
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionKeyBuilder IConventionEntityTypeBuilder.HasKey(IReadOnlyList<IConventionProperty> properties, bool fromDataAnnotation)
            => HasKey(
                properties as IReadOnlyList<Property> ?? properties.Cast<Property>().ToList(),
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionEntityTypeBuilder IConventionEntityTypeBuilder.HasNoKey(bool fromDataAnnotation)
            => HasNoKey(fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionEntityTypeBuilder IConventionEntityTypeBuilder.HasNoKey(
            IReadOnlyList<IConventionProperty> properties, bool fromDataAnnotation)
        {
            Check.NotEmpty(properties, nameof(properties));

            var key = Metadata.FindDeclaredKey(properties);
            return key != null
                ? HasNoKey(key, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention)
                : this;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        bool IConventionEntityTypeBuilder.CanRemoveKey([NotNull] IConventionKey key, bool fromDataAnnotation)
            => CanRemoveKey((Key)key, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionEntityTypeBuilder IConventionEntityTypeBuilder.HasNoKey(IConventionKey key, bool fromDataAnnotation)
            => HasNoKey((Key)key, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        bool IConventionEntityTypeBuilder.CanRemoveKey(bool fromDataAnnotation)
            => CanRemoveKey(fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionIndexBuilder IConventionEntityTypeBuilder.HasIndex(
            IReadOnlyList<string> propertyNames, bool fromDataAnnotation)
            => HasIndex(
                propertyNames,
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionIndexBuilder IConventionEntityTypeBuilder.HasIndex(
            IReadOnlyList<string> propertyNames, string name, bool fromDataAnnotation)
            => HasIndex(
                propertyNames,
                name,
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionIndexBuilder IConventionEntityTypeBuilder.HasIndex(
            IReadOnlyList<IConventionProperty> properties, bool fromDataAnnotation)
            => HasIndex(
                properties as IReadOnlyList<Property> ?? properties.Cast<Property>().ToList(),
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        IConventionIndexBuilder IConventionEntityTypeBuilder.HasIndex(
            IReadOnlyList<IConventionProperty> properties, string name, bool fromDataAnnotation)
            => HasIndex(
                properties as IReadOnlyList<Property> ?? properties.Cast<Property>().ToList(),
                name,
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionEntityTypeBuilder IConventionEntityTypeBuilder.HasNoIndex(
            IReadOnlyList<IConventionProperty> properties, bool fromDataAnnotation)
        {
            Check.NotEmpty(properties, nameof(properties));

            var index = Metadata.FindDeclaredIndex(properties);
            return index != null
                ? HasNoIndex(index, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention)
                : this;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionEntityTypeBuilder IConventionEntityTypeBuilder.HasNoIndex(IConventionIndex index, bool fromDataAnnotation)
            => HasNoIndex((Index)index, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        bool IConventionEntityTypeBuilder.CanRemoveIndex([NotNull] IConventionIndex index, bool fromDataAnnotation)
            => CanRemoveIndex((Index)index, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionForeignKeyBuilder IConventionEntityTypeBuilder.HasRelationship(
            IConventionEntityType targetEntityType, bool fromDataAnnotation)
            => HasRelationship(
                (EntityType)targetEntityType, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionForeignKeyBuilder IConventionEntityTypeBuilder.HasRelationship(
            IConventionEntityType principalEntityType, IReadOnlyList<IConventionProperty> dependentProperties, bool fromDataAnnotation)
            => HasRelationship(
                (EntityType)principalEntityType,
                dependentProperties as IReadOnlyList<Property> ?? dependentProperties.Cast<Property>().ToList(),
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionForeignKeyBuilder IConventionEntityTypeBuilder.HasRelationship(
            IConventionEntityType principalEntityType, IConventionKey principalKey, bool fromDataAnnotation)
            => HasRelationship(
                (EntityType)principalEntityType,
                (Key)principalKey,
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionForeignKeyBuilder IConventionEntityTypeBuilder.HasRelationship(
            IConventionEntityType principalEntityType,
            IReadOnlyList<IConventionProperty> dependentProperties,
            IConventionKey principalKey,
            bool fromDataAnnotation)
            => HasRelationship(
                (EntityType)principalEntityType,
                dependentProperties as IReadOnlyList<Property> ?? dependentProperties.Cast<Property>().ToList(),
                (Key)principalKey,
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionForeignKeyBuilder IConventionEntityTypeBuilder.HasRelationship(
            IConventionEntityType targetEntityType, string navigationToTargetName, bool setTargetAsPrincipal, bool fromDataAnnotation)
            => HasRelationship(
                (EntityType)targetEntityType,
                navigationToTargetName,
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention,
                setTargetAsPrincipal ? true : (bool?)null);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionForeignKeyBuilder IConventionEntityTypeBuilder.HasRelationship(
            IConventionEntityType targetEntityType, MemberInfo navigationToTarget, bool setTargetAsPrincipal, bool fromDataAnnotation)
            => HasRelationship(
                (EntityType)targetEntityType,
                navigationToTarget,
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention,
                setTargetAsPrincipal ? true : (bool?)null);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionForeignKeyBuilder IConventionEntityTypeBuilder.HasRelationship(
            IConventionEntityType targetEntityType,
            string navigationToTargetName,
            string inverseNavigationName,
            bool setTargetAsPrincipal,
            bool fromDataAnnotation)
            => HasRelationship(
                (EntityType)targetEntityType,
                navigationToTargetName, inverseNavigationName,
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention,
                setTargetAsPrincipal);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionForeignKeyBuilder IConventionEntityTypeBuilder.HasRelationship(
            IConventionEntityType targetEntityType,
            MemberInfo navigationToTarget,
            MemberInfo inverseNavigation,
            bool setTargetAsPrincipal,
            bool fromDataAnnotation)
            => HasRelationship(
                (EntityType)targetEntityType,
                navigationToTarget, inverseNavigation,
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention,
                setTargetAsPrincipal);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionForeignKeyBuilder IConventionEntityTypeBuilder.HasOwnership(
            Type targetEntityType, string navigationToTargetName, bool fromDataAnnotation)
            => HasOwnership(
                targetEntityType, navigationToTargetName,
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionForeignKeyBuilder IConventionEntityTypeBuilder.HasOwnership(
            Type targetEntityType, MemberInfo navigationToTarget, bool fromDataAnnotation)
            => HasOwnership(
                targetEntityType, navigationToTarget,
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionForeignKeyBuilder IConventionEntityTypeBuilder.HasOwnership(
            Type targetEntityType, string navigationToTargetName, string inversePropertyName, bool fromDataAnnotation)
            => HasOwnership(
                targetEntityType, navigationToTargetName, inversePropertyName,
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionForeignKeyBuilder IConventionEntityTypeBuilder.HasOwnership(
            Type targetEntityType, MemberInfo navigationToTarget, MemberInfo inverseProperty, bool fromDataAnnotation)
            => HasOwnership(
                targetEntityType, navigationToTarget, inverseProperty,
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionEntityTypeBuilder IConventionEntityTypeBuilder.HasNoRelationship(
            IReadOnlyList<IConventionProperty> properties,
            IConventionKey principalKey,
            IConventionEntityType principalEntityType,
            bool fromDataAnnotation)
        {
            Check.NotEmpty(properties, nameof(properties));
            Check.NotNull(principalKey, nameof(principalKey));
            Check.NotNull(principalEntityType, nameof(principalEntityType));

            var foreignKey = Metadata.FindDeclaredForeignKey(properties, principalKey, principalEntityType);
            return foreignKey != null
                ? HasNoRelationship(foreignKey, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention)
                : this;
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionEntityTypeBuilder IConventionEntityTypeBuilder.HasNoRelationship(
            IConventionForeignKey foreignKey, bool fromDataAnnotation)
            => HasNoRelationship(
                (ForeignKey)foreignKey,
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        bool IConventionEntityTypeBuilder.CanRemoveRelationship([NotNull] IConventionForeignKey foreignKey, bool fromDataAnnotation)
            => CanRemoveForeignKey((ForeignKey)foreignKey,
                    fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        bool IConventionEntityTypeBuilder.CanHaveNavigation(string navigationName, bool fromDataAnnotation)
            => CanHaveNavigation(
                navigationName,
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <inheritdoc />
        [DebuggerStepThrough]
        IConventionSkipNavigationBuilder IConventionEntityTypeBuilder.HasSkipNavigation(
            MemberInfo navigationToTarget,
            IConventionEntityType targetEntityType,
            bool collection,
            bool onDependent,
            bool fromDataAnnotation)
            => HasSkipNavigation(
                MemberIdentity.Create(navigationToTarget),
                (EntityType)targetEntityType,
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention,
                collection,
                onDependent);

        /// <inheritdoc />
        [DebuggerStepThrough]
        IConventionSkipNavigationBuilder IConventionEntityTypeBuilder.HasSkipNavigation(
            string navigationName,
            IConventionEntityType targetEntityType,
            bool collection,
            bool onDependent,
            bool fromDataAnnotation)
            => HasSkipNavigation(
                MemberIdentity.Create(navigationName),
                (EntityType)targetEntityType,
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention,
                collection,
                onDependent);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionEntityTypeBuilder IConventionEntityTypeBuilder.HasQueryFilter(LambdaExpression filter, bool fromDataAnnotation)
            => HasQueryFilter(filter, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        bool IConventionEntityTypeBuilder.CanSetQueryFilter(LambdaExpression filter, bool fromDataAnnotation)
            => CanSetQueryFilter(filter, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        [Obsolete]
        IConventionEntityTypeBuilder IConventionEntityTypeBuilder.HasDefiningQuery(LambdaExpression query, bool fromDataAnnotation)
            => HasDefiningQuery(query, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        [Obsolete]
        bool IConventionEntityTypeBuilder.CanSetDefiningQuery(LambdaExpression query, bool fromDataAnnotation)
            => CanSetDefiningQuery(query, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionEntityTypeBuilder IConventionEntityTypeBuilder.HasChangeTrackingStrategy(
            ChangeTrackingStrategy? changeTrackingStrategy, bool fromDataAnnotation)
            => HasChangeTrackingStrategy(
                changeTrackingStrategy, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        bool IConventionEntityTypeBuilder.CanSetChangeTrackingStrategy(
            ChangeTrackingStrategy? changeTrackingStrategy, bool fromDataAnnotation)
            => CanSetChangeTrackingStrategy(
                changeTrackingStrategy, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionEntityTypeBuilder IConventionEntityTypeBuilder.UsePropertyAccessMode(
            PropertyAccessMode? propertyAccessMode, bool fromDataAnnotation)
            => UsePropertyAccessMode(
                propertyAccessMode, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        bool IConventionEntityTypeBuilder.CanSetPropertyAccessMode(PropertyAccessMode? propertyAccessMode, bool fromDataAnnotation)
            => CanSetPropertyAccessMode(
                propertyAccessMode, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionDiscriminatorBuilder IConventionEntityTypeBuilder.HasDiscriminator(bool fromDataAnnotation)
            => HasDiscriminator(
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionDiscriminatorBuilder IConventionEntityTypeBuilder.HasDiscriminator(Type type, bool fromDataAnnotation)
            => HasDiscriminator(name: null, Check.NotNull(type, nameof(type)),
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionDiscriminatorBuilder IConventionEntityTypeBuilder.HasDiscriminator(string name, bool fromDataAnnotation)
            => HasDiscriminator(Check.NotEmpty(name, nameof(name)), type: null,
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionDiscriminatorBuilder IConventionEntityTypeBuilder.HasDiscriminator(string name, Type type, bool fromDataAnnotation)
            => HasDiscriminator(Check.NotEmpty(name, nameof(name)), Check.NotNull(type, nameof(type)),
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionDiscriminatorBuilder IConventionEntityTypeBuilder.HasDiscriminator(MemberInfo memberInfo, bool fromDataAnnotation)
            => HasDiscriminator(
                memberInfo, fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        IConventionEntityTypeBuilder IConventionEntityTypeBuilder.HasNoDiscriminator(bool fromDataAnnotation)
            => HasNoDiscriminator(fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        bool IConventionEntityTypeBuilder.CanSetDiscriminator(string name, bool fromDataAnnotation)
            => CanSetDiscriminator(name, type: null,
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        bool IConventionEntityTypeBuilder.CanSetDiscriminator(Type type, bool fromDataAnnotation)
            => CanSetDiscriminator(name: null, type,
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        bool IConventionEntityTypeBuilder.CanSetDiscriminator(string name, Type type, bool fromDataAnnotation)
            => CanSetDiscriminator(name, type,
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        bool IConventionEntityTypeBuilder.CanSetDiscriminator([NotNull] MemberInfo memberInfo, bool fromDataAnnotation)
            => CanSetDiscriminator(Check.NotNull(memberInfo, nameof(memberInfo)).GetSimpleMemberName(), memberInfo.GetMemberType(),
                fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        [DebuggerStepThrough]
        bool IConventionEntityTypeBuilder.CanRemoveDiscriminator(bool fromDataAnnotation)
            => CanRemoveDiscriminator(fromDataAnnotation ? ConfigurationSource.DataAnnotation : ConfigurationSource.Convention);
    }
}
