using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using TeacherScheduleApp.Services;

namespace TeacherScheduleApp.ViewModels
{
    public sealed class ImportBatchItemViewModel : ReactiveObject
    {
        private readonly MainWindowViewModel _owner;

        public string Id { get; }
        public string Label { get; }
        public DateTime RangeStart { get; }
        public DateTime RangeEnd { get; }
        public int EventsCount { get; }

        public ReactiveCommand<Unit, Unit> DeleteCommand { get; }

        public ImportBatchItemViewModel(EventService.ImportBatchInfo model, MainWindowViewModel owner)
        {
            _owner = owner;

            Id = model.Id;
            Label = model.Label;
            RangeStart = model.RangeStart;
            RangeEnd = model.RangeEnd;
            EventsCount = model.EventsCount;

            DeleteCommand = ReactiveCommand.CreateFromTask(() =>
                _owner.DeleteImportByIdAsync(this));
        }
    }
}
