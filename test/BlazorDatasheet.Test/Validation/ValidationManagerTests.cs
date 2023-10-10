using System.Collections.Generic;
using System.Linq;
using BlazorDatasheet.Data;
using BlazorDatasheet.DataStructures.Geometry;
using BlazorDatasheet.Interfaces;
using BlazorDatasheet.Validation;
using FluentAssertions;
using NUnit.Framework;

namespace BlazorDatasheet.Test.Validation;

public class ValidationManagerTests
{
    [Test]
    public void Add_Validator_Returns_Validator_In_Region()
    {
        var vm = new ValidationManager();
        vm.Add(new NumberValidator(true), new Region(0, 5, 0, 5));
        vm.GetValidators(0, 0).Should().NotBeEmpty();
        vm.GetValidators(1, 1).Should().NotBeEmpty();
        vm.GetValidators(6, 0).Should().BeEmpty();
    }

    [Test]
    public void Validation_Validates_On_Cell_With_Validators()
    {
        var vm = new ValidationManager();
        var validator = new AlwaysFalseValidator(true);
        vm.Add(validator, new Region(0, 2, 0, 2));
        var validationInside = vm.Validate(0, 0, 0);
        validationInside.IsValid.Should().BeFalse();
        validationInside.IsStrictFail.Should().Be(validator.IsStrict);
        validationInside.FailMessages.Should().ContainSingle(validator.Message);

        var validationOutside = vm.Validate(0, 3, 0);
        validationOutside.IsValid.Should().BeTrue();
        validationOutside.IsStrictFail.Should().BeFalse();
        validationOutside.FailMessages.Should().BeEmpty();
    }

    [Test]
    public void Remove_Validator_From_Region_Results_In_No_Validation()
    {
        var falseValidator = new AlwaysFalseValidator(true);
        var vm = new ValidationManager();
        vm.Add(falseValidator, new Region(0, 5, 0, 5));
        vm.Remove(falseValidator, new Region(2, 3, 2, 3));
        vm.Validate(-1, 0, 0).IsValid.Should().BeFalse();
        vm.Validate(-1, 2, 2).IsValid.Should().BeTrue();
    }

    [Test]
    public void Insert_Row_At_Top_Of_Validator_Shifts_It_Down()
    {
        var sheet = new Sheet(4, 4);
        var val = new SourceValidator(new List<string>() { "A", "B" }, false);
        sheet.Validation.Add(val, new Region(2, 2));
        sheet.InsertRowAt(2);
        sheet.Validation.GetValidators(1, 2).Should().BeEmpty();
        sheet.Validation.GetValidators(2, 2).Should().BeEmpty();
        sheet.Validation.GetValidators(3, 2).First().Should().BeSameAs(val);

        sheet.Commands.Undo();
        sheet.Validation.GetValidators(3, 2).Should().BeEmpty();
        sheet.Validation.GetValidators(2, 2).First().Should().BeSameAs(val);
    }
    
    [Test]
    public void Insert_Col_At_Left_Of_Validator_Shifts_It_Right()
    {
        var sheet = new Sheet(4, 4);
        var val = new SourceValidator(new List<string>() { "A", "B" }, false);
        sheet.Validation.Add(val, new Region(2, 2));
        sheet.InsertColAt(2);
        sheet.Validation.GetValidators(2, 1).Should().BeEmpty();
        sheet.Validation.GetValidators(2, 2).Should().BeEmpty();
        sheet.Validation.GetValidators(2, 3).First().Should().BeSameAs(val);

        sheet.Commands.Undo();
        sheet.Validation.GetValidators(2, 3).Should().BeEmpty();
        sheet.Validation.GetValidators(2, 2).First().Should().BeSameAs(val);
    }
    
    [Test]
    public void Insert_Col_At_Left_Of_Validator_WhenAtCol0_Shifts_It_Right()
    {
        var sheet = new Sheet(4, 4);
        var val = new SourceValidator(new List<string>() { "A", "B" }, false);
        sheet.Validation.Add(val, new Region(2, 0));
        sheet.InsertColAt(0);
        sheet.Validation.GetValidators(2, 0).Should().BeEmpty();
        sheet.Validation.GetValidators(2, 1).First().Should().BeSameAs(val);

        sheet.Commands.Undo();
        sheet.Validation.GetValidators(2, 1).Should().BeEmpty();
        sheet.Validation.GetValidators(2, 0).First().Should().BeSameAs(val);
    }

    internal class AlwaysFalseValidator : IDataValidator
    {
        public AlwaysFalseValidator(bool isStrict)
        {
            IsStrict = isStrict;
        }

        public bool IsValid(object? value)
        {
            return false;
        }

        public bool IsStrict { get; }
        public string Message => "Always invalid";
    }

    internal class AlwaysTrueValidator : IDataValidator
    {
        public AlwaysTrueValidator(bool isStrict)
        {
            IsStrict = isStrict;
        }

        public bool IsValid(object? value)
        {
            return false;
        }

        public bool IsStrict { get; }
        public string Message => "Always valid";
    }
}